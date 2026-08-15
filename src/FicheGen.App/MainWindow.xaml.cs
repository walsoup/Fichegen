// -----------------------------------------------------------------------------
// FicheGen — Coque principale (Fluent 2)
// Barre de titre personnalisée, persistance de la fenêtre, raccourcis clavier.
// -----------------------------------------------------------------------------

using FicheGen.App.Services;
using FicheGen.App.ViewModels;
using FicheGen.App.Views;
using FicheGen.App.Views.Controls;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using Color = Windows.UI.Color;
using Colors = Microsoft.UI.Colors;

namespace FicheGen.App;

public sealed partial class MainWindow : Window
{
    // ----- Géométrie de fenêtre -----
    private const int MinWindowWidthDip = 1024;
    private const int MinWindowHeightDip = 640;
    private const int DefaultWindowWidthDip = 1280;
    private const int DefaultWindowHeightDip = 840;
    private const int PlacementSentinel = -32000;

    // ----- Win32 -----
    private const int GwlpWndProc = -4;
    private const uint WmGetMinMaxInfo = 0x0024;

    private readonly ISettingsStore _settingsStore;
    private readonly ShellStateSettings _shellState;
    private readonly DispatcherTimer _hintTimer;
    private readonly IntPtr _hwnd;
    private readonly Microsoft.UI.WindowId _windowId;
    private readonly AppWindow _appWindow;

    private WndProcDelegate? _wndProcDelegate;
    private IntPtr _oldWndProc;
    private RectInt32 _lastNormalBounds;
    private bool _isPreviewVisible = true;
    private bool _wasOnSettings;

    public MainWindow()
    {
        InitializeComponent();

        Title = "PROFstudio";

        _settingsStore = App.Services.GetRequiredService<ISettingsStore>();
        _shellState = LoadShellState();

        // ----- Fenêtre native / AppWindow -----
        _hwnd = WindowNative.GetWindowHandle(this);
        _windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(_windowId);
        _appWindow.Title = "PROFstudio";

        // ----- Barre de titre personnalisée -----
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        if (Content?.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed += (_, _) => UpdateTitleBarLayout();
        }

        // ----- Arrière-plan Fluent : Mica (Windows 11) / Acrylic (Windows 10) -----
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            }
            else if (DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not initialize SystemBackdrop.");
        }

        // ----- Taille minimale 1024 × 640 -----
        HookWindowProc();

        // ----- Thème (Clair / Sombre / Système) -----
        ApplyTheme(_shellState.Theme, persist: false);
        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtonColors();

        // ----- État initial de l'Assistant IA -----
        AssistantToggleButton.IsChecked = _shellState.IsAssistantVisible;

        // ----- Ctrl + , (non déclarable en XAML) -----
        var commaAccelerator = new KeyboardAccelerator
        {
            Key = (VirtualKey)188,
            Modifiers = VirtualKeyModifiers.Control
        };
        commaAccelerator.Invoked += OnCtrlCommaInvoked;
        RootGrid.KeyboardAccelerators.Add(commaAccelerator);

        // ----- Minuteur des astuces contextuelles -----
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _hintTimer.Tick += (_, _) => { _hintTimer.Stop(); ShellHintTip.IsOpen = false; };

        // ----- Restauration de la géométrie de la fenêtre -----
        RestoreWindowPlacement();
        UpdateTitleBarLayout();

        // ----- Badges dynamiques (IA, profil) -----
        var readinessService = App.Services.GetService<IReadinessService>();
        if (readinessService != null)
        {
            readinessService.ReadinessChanged += (s, e) =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateAiChipUi(readinessService));
            };
        }
        RefreshAiChip();
        RefreshTeacherBadge();

        // ----- Navigation initiale : dernière section utilisée -----
        NavView.SelectedItem = FindNavItem(_shellState.LastNavigationTag) ?? NavView.MenuItems[0];

        var historyVm = App.Services.GetService<HistoryViewModel>();
        if (historyVm != null)
        {
            historyVm.DocumentOpened += (s, item) =>
            {
                var resultVm = App.Services.GetService<ResultViewModel>();
                if (resultVm != null)
                {
                    if (!string.IsNullOrEmpty(item.Model.SourceJson))
                    {
                        try
                        {
                            var doc = System.Text.Json.JsonSerializer.Deserialize<GeneratedDocument>(item.Model.SourceJson);
                            if (doc != null)
                            {
                                resultVm.LoadDocument(doc, item.Html ?? string.Empty, item.StylePresetId);
                            }
                        }
                        catch { }
                    }
                    else if (string.IsNullOrEmpty(resultVm.CurrentHtml) && !string.IsNullOrEmpty(item.Html))
                    {
                        resultVm.CurrentHtml = item.Html;
                    }
                }

                var tag = item.TypeKey switch
                {
                    "evaluation" => "EvaluationPage",
                    "quiz" => "QuizPage",
                    _ => "FichePage"
                };
                NavigateToTag(tag);
            };
        }

        var resultViewModel = App.Services.GetService<ResultViewModel>();
        if (resultViewModel != null)
        {
            resultViewModel.CreateEvaluationRequested += (s, doc) =>
            {
                var evalVm = App.Services.GetService<EvaluationViewModel>();
                if (evalVm != null)
                {
                    evalVm.ClassLevel = doc.Metadata.ClassLevel ?? "CM2";
                    evalVm.Subject = doc.Metadata.Subject ?? "Mathématiques";
                    evalVm.Topics = doc.Metadata.Title ?? string.Empty;
                }
                NavigateToTag("EvaluationPage");
            };

            resultViewModel.CreateQuizRequested += (s, doc) =>
            {
                var quizVm = App.Services.GetService<QuizViewModel>();
                if (quizVm != null)
                {
                    quizVm.ClassLevel = doc.Metadata.ClassLevel ?? "CM2";
                    quizVm.Subject = doc.Metadata.Subject ?? "Mathématiques";
                    quizVm.Topic = doc.Metadata.Title ?? string.Empty;
                }
                NavigateToTag("QuizPage");
            };
        }

        RootGrid.Loaded += OnMainWindowLoaded;
        _appWindow.Changed += OnAppWindowChanged;
        Activated += OnMainWindowActivated;
        Closed += OnMainWindowClosed;
    }

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnMainWindowLoaded;

        if (ContentFrame.Content == null)
        {
            var initialTag = _shellState.LastNavigationTag ?? "FichePage";
            var initialItem = FindNavItem(initialTag) ?? (NavView.MenuItems.Count > 0 ? NavView.MenuItems[0] as NavigationViewItem : null);
            if (initialItem != null)
            {
                NavView.SelectedItem = initialItem;
            }
            var pageType = (initialItem?.Tag?.ToString() ?? initialTag) switch
            {
                "FichePage" => typeof(FichePage),
                "EvaluationPage" => typeof(EvaluationPage),
                "QuizPage" => typeof(QuizPage),
                "HistoryPage" => typeof(HistoryPage),
                "SettingsPage" => typeof(SettingsPage),
                _ => typeof(FichePage)
            };
            NavigateTo(pageType, initialItem?.Tag?.ToString() ?? initialTag);
        }
    }

    // ==========================================================================
    //  Premier démarrage : WebView2 + expérience de première exécution (RGPD)
    // ==========================================================================
    private async Task<XamlRoot?> EnsureXamlRootAsync()
    {
        if (RootGrid.XamlRoot != null) return RootGrid.XamlRoot;
        if (Content?.XamlRoot != null) return Content.XamlRoot;

        var tcs = new TaskCompletionSource<XamlRoot?>();
        RoutedEventHandler loadedHandler = null!;
        loadedHandler = (s, e) =>
        {
            RootGrid.Loaded -= loadedHandler;
            tcs.TrySetResult(RootGrid.XamlRoot ?? Content?.XamlRoot);
        };
        RootGrid.Loaded += loadedHandler;

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        if (completedTask == tcs.Task)
        {
            return await tcs.Task;
        }

        RootGrid.Loaded -= loadedHandler;
        return RootGrid.XamlRoot ?? Content?.XamlRoot;
    }

    private async void OnMainWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnMainWindowActivated;

        UpdateTitleBarLayout();
        UpdateCaptionButtonColors();

        var xamlRoot = await EnsureXamlRootAsync();
        if (xamlRoot == null)
        {
            Log.Warning("Boîte de dialogue annulée: XamlRoot introuvable.");
            return;
        }

        var dialogService = App.Services.GetService<DialogService>();
        dialogService?.Initialize(xamlRoot);

        // Vérification du composant WebView2
        var checker = new WebView2RuntimeChecker();
        if (!checker.IsWebView2Available())
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = "Composant WebView2 requis",
                Content = "Le composant WebView2 de Microsoft Edge n'est pas détecté. PROFstudio l'utilise pour afficher l'aperçu vectoriel des fiches.",
                PrimaryButtonText = "Télécharger le programme d'installation",
                SecondaryButtonText = "Plus tard",
                DefaultButton = ContentDialogButton.Primary
            };

            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary)
            {
                var uri = new Uri(checker.GetDownloadUrl());
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }

        // Première exécution & transparence RGPD
        var settingsStore = App.Services.GetRequiredService<ISettingsStore>();
        var settings = settingsStore.GetSettings<AppSettings>();
        if (!settings.IsFirstRunCompleted)
        {
            var firstRunDlg = new FirstRunDialog
            {
                XamlRoot = xamlRoot
            };

            var res = await firstRunDlg.ShowAsync();
            settings.IsFirstRunCompleted = true; // Persiste le choix même en cas d'ignorance (UX-08)
            if (res == ContentDialogResult.Primary)
            {
                settings.Features.Telemetry = firstRunDlg.TelemetryEnabled;
                if (!string.IsNullOrWhiteSpace(firstRunDlg.GuidesPath))
                {
                    settings.Folders.GuidesDir = firstRunDlg.GuidesPath;
                }
                if (!string.IsNullOrWhiteSpace(firstRunDlg.SelectedProviderKey))
                {
                    settings.Ai.GlobalProvider = firstRunDlg.SelectedProviderKey;
                }
                if (!string.IsNullOrWhiteSpace(firstRunDlg.ApiKey))
                {
                    var credentialStore = App.Services.GetRequiredService<ICredentialStore>();
                    var keyName = firstRunDlg.SelectedProviderKey switch
                    {
                        "openai" => "openai_api_key",
                        _ => "gemini_api_key"
                    };
                    credentialStore.Set(keyName, firstRunDlg.ApiKey);
                }
                await settingsStore.SaveSettingsAsync(settings);

                ShowHint("Bienvenue dans PROFstudio 👋",
                    "Votre espace est prêt ! Cliquez sur « Nouvelle fiche » pour démarrer votre premier document.");
            }
            else
            {
                await settingsStore.SaveSettingsAsync(settings);
            }
            RefreshAiChip();
        }
    }

    // ==========================================================================
    //  Navigation
    // ==========================================================================
    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateTo(typeof(SettingsPage), "SettingsPage");
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString() ?? "FichePage";
            Type pageType = tag switch
            {
                "FichePage" => typeof(FichePage),
                "EvaluationPage" => typeof(EvaluationPage),
                "QuizPage" => typeof(QuizPage),
                "HistoryPage" => typeof(HistoryPage),
                "SettingsPage" => typeof(SettingsPage),
                _ => typeof(FichePage)
            };

            NavigateTo(pageType, tag);
        }
    }

    private void NavigateTo(Type pageType, string tag)
    {
        if (ContentFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());

        if (ContentFrame.Content is Page page)
        {
            page.DataContext = tag switch
            {
                "FichePage" => App.Services.GetRequiredService<FicheFormViewModel>(),
                "EvaluationPage" => App.Services.GetRequiredService<EvaluationViewModel>(),
                "QuizPage" => App.Services.GetRequiredService<QuizViewModel>(),
                "HistoryPage" => App.Services.GetRequiredService<HistoryViewModel>(),
                "SettingsPage" => App.Services.GetRequiredService<SettingsViewModel>(),
                _ => App.Services.GetRequiredService<FicheFormViewModel>()
            };

            // Ré-applique la visibilité du volet Assistant sur la nouvelle page
            ToggleAssistantVisibility(AssistantToggleButton.IsChecked == true);
        }

        StatusSectionText.Text = tag switch
        {
            "FichePage" => "Section : Fiche pédagogique",
            "EvaluationPage" => "Section : Évaluation",
            "QuizPage" => "Section : Quiz",
            "HistoryPage" => "Section : Historique",
            "SettingsPage" => "Section : Paramètres",
            _ => "Section : Fiche pédagogique"
        };

        _shellState.LastNavigationTag = tag;

        // En revenant des Paramètres, on rafraîchit la puce IA et le profil
        if (_wasOnSettings)
        {
            RefreshAiChip();
            RefreshTeacherBadge();
            _wasOnSettings = false;
        }
        if (tag == "SettingsPage")
        {
            _wasOnSettings = true;
        }

        SaveShellState();
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // L'entrée « Aide » ne change pas la sélection (SelectsOnInvoked = False)
        if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag } && tag == "Help")
        {
            ShowShortcutsDialog();
        }
    }

    private NavigationViewItem? FindNavItem(string? tag) =>
        NavView.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.Ordinal));

    private void NavigateToTag(string tag)
    {
        if (tag == "SettingsPage")
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        var item = FindNavItem(tag);
        if (item is not null)
        {
            NavView.SelectedItem = item;
        }
    }

    private void OnGlobalSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText;

        void ApplyToHistory(HistoryPage hp)
        {
            hp.SetSearchQuery(query);
            hp.FocusSearchBox();
        }

        if (ContentFrame.Content is HistoryPage current)
        {
            ApplyToHistory(current);
        }
        else
        {
            void handler(object? s, NavigationEventArgs e)
            {
                ContentFrame.Navigated -= handler;
                if (ContentFrame.Content is HistoryPage hp) ApplyToHistory(hp);
            }
            ContentFrame.Navigated += handler;
            NavigateToTag("HistoryPage");
        }

        SetStatus("🔍 Recherche dans l'historique");
        GlobalSearchBox.Text = string.Empty;
    }

    // ==========================================================================
    //  Volet Assistant IA
    // ==========================================================================
    private void OnAssistantToggleClicked(object sender, RoutedEventArgs e)
    {
        SetAssistantVisible(AssistantToggleButton.IsChecked == true);
    }

    private void SetAssistantVisible(bool visible)
    {
        ToggleAssistantVisibility(visible);
        _shellState.IsAssistantVisible = visible;
        SaveShellState();
    }

    private void ToggleAssistantVisibility(bool isVisible)
    {
        if (ContentFrame.Content is IAssistantHostPage hostPage)
        {
            hostPage.SetAssistantVisible(isVisible);
        }
    }

    private async void OnAiChipClicked(object sender, RoutedEventArgs e)
    {
        var readiness = App.Services.GetService<IReadinessService>();
        if (readiness != null && (readiness.State == ReadinessState.Degraded || readiness.State == ReadinessState.Offline))
        {
            await readiness.RefreshAsync();
            UpdateAiChipUi(readiness);
        }
        else
        {
            NavigateToTag("SettingsPage");
        }
    }

    private void OnNewFicheClicked(object sender, RoutedEventArgs e) => CreateNewFiche();

    private void OnSearchClicked(object sender, RoutedEventArgs e) => FocusHistorySearch();

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => NavigateToTag("SettingsPage");

    private void OnShortcutsClicked(object sender, RoutedEventArgs e) => ShowShortcutsDialog();

    private void OnProfileSettingsClicked(object sender, RoutedEventArgs e)
    {
        ProfileButton.Flyout?.Hide();
        NavigateToTag("SettingsPage");
    }

    private void OnProfileShortcutsClicked(object sender, RoutedEventArgs e)
    {
        ProfileButton.Flyout?.Hide();
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(150);
            ShowShortcutsDialog();
        });
    }

    private void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        ProfileButton.Flyout?.Hide();
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(150);
            ShowAboutDialog();
        });
    }

    private void CreateNewFiche()
    {
        NavigateToTag("FichePage");
        SetStatus("📄 Nouvelle fiche");

        if (ContentFrame.Content is Page { DataContext: object dc } &&
            ExecuteMatchingCommand(dc, out _, "ResetFormCommand", "NewFicheCommand"))
        {
            ShowHint("Nouvelle fiche", "Le formulaire est prêt — à vous de jouer ! ✨");
        }
    }

    private void FocusHistorySearch()
    {
        if (ContentFrame.Content is HistoryPage hp)
        {
            hp.FocusSearchBox();
            SetStatus("🔍 Recherche dans l'historique");
            return;
        }

        void handler(object? s, NavigationEventArgs e)
        {
            ContentFrame.Navigated -= handler;
            if (ContentFrame.Content is HistoryPage h) h.FocusSearchBox();
        }
        ContentFrame.Navigated += handler;
        NavigateToTag("HistoryPage");
        SetStatus("🔍 Recherche dans l'historique");
    }

    // ==========================================================================
    //  Thème (Clair / Sombre / Système)
    // ==========================================================================
    private void OnThemeItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem item && item.Tag is string theme)
        {
            ApplyTheme(theme);
        }
    }

    private void ApplyTheme(string theme, bool persist = true)
    {
        _shellState.Theme = theme is "Light" or "Dark" or "System" ? theme : "System";

        RootGrid.RequestedTheme = _shellState.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        ThemeSystemItem.IsChecked = _shellState.Theme == "System";
        ThemeLightItem.IsChecked = _shellState.Theme == "Light";
        ThemeDarkItem.IsChecked = _shellState.Theme == "Dark";

        UpdateCaptionButtonColors();

        if (persist)
        {
            SaveShellState();
        }
    }

    private void UpdateCaptionButtonColors()
    {
        try
        {
            var tb = _appWindow.TitleBar;
            bool dark = RootGrid.ActualTheme == ElementTheme.Dark;

            tb.BackgroundColor = Colors.Transparent;
            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.InactiveBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;

            tb.ForegroundColor = dark ? Colors.White : Colors.Black;
            tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;

            var inactive = dark ? Color.FromArgb(255, 160, 160, 160) : Color.FromArgb(255, 110, 110, 110);
            tb.InactiveForegroundColor = inactive;
            tb.ButtonInactiveForegroundColor = inactive;

            tb.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
            tb.ButtonHoverBackgroundColor = dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0);
            tb.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
            tb.ButtonPressedBackgroundColor = dark ? Color.FromArgb(56, 255, 255, 255) : Color.FromArgb(56, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Couleurs des boutons de la barre de titre non appliquées.");
        }
    }

    // ==========================================================================
    //  Gestion de la fenêtre : insets, taille minimale, persistance
    // ==========================================================================
    private void UpdateTitleBarLayout()
    {
        try
        {
            double scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1;

            var tb = _appWindow.TitleBar;
            AppTitleBar.Padding = new Thickness(
                Math.Max(tb.LeftInset, 0) / scale, 0,
                Math.Max(tb.RightInset, 0) / scale, 0);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Mise à jour des insets de la barre de titre impossible.");
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is OverlappedPresenter op && op.State == OverlappedPresenterState.Restored)
        {
            _lastNormalBounds = new RectInt32(
                sender.Position.X, sender.Position.Y,
                sender.Size.Width, sender.Size.Height);
        }

        if (args.DidSizeChange || args.DidPresenterChange)
        {
            UpdateTitleBarLayout();
        }
    }

    private void RestoreWindowPlacement()
    {
        try
        {
            double scale = GetDpiForWindow(_hwnd) / 96.0;
            if (scale <= 0) scale = 1;

            if (_shellState.X == PlacementSentinel || _shellState.Y == PlacementSentinel)
            {
                // Première exécution : taille par défaut, centrée sur l'écran
                int w = (int)(DefaultWindowWidthDip * scale);
                int h = (int)(DefaultWindowHeightDip * scale);
                var wa = DisplayArea.GetFromWindowId(_windowId, DisplayAreaFallback.Primary).WorkArea;
                w = Math.Min(w, wa.Width);
                h = Math.Min(h, wa.Height);
                _appWindow.MoveAndResize(new RectInt32(
                    wa.X + (wa.Width - w) / 2,
                    wa.Y + (wa.Height - h) / 2,
                    w, h));
            }
            else
            {
                int w = Math.Max(_shellState.Width, (int)(MinWindowWidthDip * scale));
                int h = Math.Max(_shellState.Height, (int)(MinWindowHeightDip * scale));
                var rect = new RectInt32(_shellState.X, _shellState.Y, w, h);

                if (IsRectOnScreen(rect))
                {
                    _appWindow.MoveAndResize(rect);
                }
                else
                {
                    // La position enregistrée est hors écran : on restaure au moins la taille
                    _appWindow.Resize(new SizeInt32(w, h));
                }
            }

            if (_shellState.IsMaximized && _appWindow.Presenter is OverlappedPresenter op)
            {
                op.Maximize();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Restauration de la géométrie de la fenêtre impossible.");
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            if (_appWindow.Presenter is OverlappedPresenter op)
            {
                _shellState.IsMaximized = op.State == OverlappedPresenterState.Maximized;

                if (op.State == OverlappedPresenterState.Restored)
                {
                    _shellState.X = _appWindow.Position.X;
                    _shellState.Y = _appWindow.Position.Y;
                    _shellState.Width = _appWindow.Size.Width;
                    _shellState.Height = _appWindow.Size.Height;
                }
                else if (_lastNormalBounds.Width > 0)
                {
                    // Fenêtre agrandie : on conserve les dernières dimensions « normales »
                    _shellState.X = _lastNormalBounds.X;
                    _shellState.Y = _lastNormalBounds.Y;
                    _shellState.Width = _lastNormalBounds.Width;
                    _shellState.Height = _lastNormalBounds.Height;
                }
            }

            _shellState.IsAssistantVisible = AssistantToggleButton.IsChecked == true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Lecture de la géométrie finale impossible.");
        }

        SaveShellState();
    }

    private static bool IsRectOnScreen(RectInt32 rect)
    {
        try
        {
            foreach (var area in DisplayArea.FindAll())
            {
                var wa = area.WorkArea;
                bool intersects =
                    rect.X < wa.X + wa.Width && rect.X + rect.Width > wa.X &&
                    rect.Y < wa.Y + wa.Height && rect.Y + rect.Height > wa.Y;
                if (intersects) return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsWindows11OrGreater() =>
        Environment.OSVersion.Version >= new Version(10, 0, 22000);

    // ----- Sous-classement Win32 pour la taille minimale (1024 × 640) -----

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newProc);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr newProc);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, newProc)
            : SetWindowLong32(hWnd, nIndex, newProc);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr prevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    private void HookWindowProc()
    {
        try
        {
            _wndProcDelegate = WndProc;
            _oldWndProc = SetWindowLongPtr(_hwnd, GwlpWndProc,
                Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Impossible d'installer la taille minimale de fenêtre.");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmGetMinMaxInfo)
        {
            double scale = GetDpiForWindow(hWnd) / 96.0;
            if (scale <= 0) scale = 1;

            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize = new POINT
            {
                X = (int)Math.Ceiling(MinWindowWidthDip * scale),
                Y = (int)Math.Ceiling(MinWindowHeightDip * scale)
            };
            Marshal.StructureToPtr(info, lParam, true);
        }
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    // ==========================================================================
    //  Persistance de l'état de la coque
    // ==========================================================================
    private ShellStateSettings LoadShellState()
    {
        try
        {
            return _settingsStore.GetSettings<ShellStateSettings>() ?? new ShellStateSettings();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Impossible de charger l'état de la coque.");
            return new ShellStateSettings();
        }
    }

    private void SaveShellState()
    {
        _ = SaveShellStateCoreAsync();

        async Task SaveShellStateCoreAsync()
        {
            try
            {
                await _settingsStore.SaveSettingsAsync(_shellState).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Impossible d'enregistrer l'état de la coque.");
            }
        }
    }

    // ==========================================================================
    //  Badges dynamiques : puce IA & profil enseignant (IReadinessService UX-05)
    // ==========================================================================
    public void RefreshAiChip()
    {
        var readiness = App.Services.GetService<IReadinessService>();
        if (readiness == null) return;

        _ = readiness.RefreshAsync();
        UpdateAiChipUi(readiness);
    }

    private void UpdateAiChipUi(IReadinessService readiness)
    {
        AiStatusGlyph.Text = readiness.ShapeGlyph;
        AiProviderChipText.Text = readiness.StatusTitle;

        switch (readiness.State)
        {
            case ReadinessState.Ready:
                AiStatusGlyph.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
                break;
            case ReadinessState.Checking:
                AiStatusGlyph.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                break;
            case ReadinessState.Degraded:
            case ReadinessState.Offline:
                AiStatusGlyph.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8));
                break;
            case ReadinessState.Blocked:
                AiStatusGlyph.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));
                break;
            case ReadinessState.NotConfigured:
            default:
                AiStatusGlyph.Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
                break;
        }

        ToolTipService.SetToolTip(AiProviderChip, readiness.StatusDetails);
        AutomationProperties.SetName(AiProviderChip, $"{readiness.StatusTitle}. {readiness.StatusDetails}");
    }

    private void RefreshTeacherBadge()
    {
        try
        {
            var settings = _settingsStore.GetSettings<AppSettings>();
            var name = string.IsNullOrWhiteSpace(settings.Defaults.TeacherName) ? "Enseignant·e" : settings.Defaults.TeacherName;
            var school = string.IsNullOrWhiteSpace(settings.Defaults.SchoolName) ? "Compte local — aucune donnée partagée" : settings.Defaults.SchoolName;

            TeacherPersonPicture.DisplayName = name;
            TeacherPersonPictureLarge.DisplayName = name;
            ProfileNameText.Text = name;
            ProfileSubtext.Text = school;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Lecture du profil enseignant impossible.");
        }
    }

    /// <summary>
    /// Recherche réflexive (profondeur limitée) d'une propriété texte dans le graphe
    /// des paramètres — permet une puce IA réellement dynamique sans couplage fort.
    /// </summary>
    private static string? FindStringProperty(object? root, string key)
    {
        if (root is null) return null;

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Target, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (target, depth) = queue.Dequeue();
            if (!visited.Add(target)) continue;

            foreach (var prop in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

                object? value;
                try { value = prop.GetValue(target); }
                catch { continue; }

                if (value is string s)
                {
                    if (!string.IsNullOrWhiteSpace(s) &&
                        prop.Name.Contains(key, StringComparison.OrdinalIgnoreCase))
                    {
                        return s;
                    }
                }
                else if (value is not null && depth < 3 &&
                         value.GetType().IsClass &&
                         value is not System.Collections.IEnumerable)
                {
                    queue.Enqueue((value, depth + 1));
                }
            }
        }
        return null;
    }

    // ==========================================================================
    //  Raccourcis clavier — dispatchers
    // ==========================================================================
    private void OnCtrlGInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ContentFrame.Content is Page { DataContext: object dc })
        {
            if (ExecuteMatchingCommand(dc, out bool found,
                    "GenerateFicheCommand", "GenerateEvaluationCommand", "GenerateQuizCommand", "GenerateCommand"))
            {
                SetStatus("⏳ Génération en cours…");
                return;
            }

            ShowHint("Génération", found
                ? "Une génération est déjà en cours… Patience ! ⏳"
                : "Aucune génération n'est disponible sur cette page.");
        }
        else
        {
            NavigateToTag("FichePage");
            ShowHint("Génération", "Ouvrez d'abord une page de création (Fiche, Évaluation ou Quiz).");
        }
    }

    private void OnEscInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShellHintTip.IsOpen = false;

        if (ContentFrame.Content is Page { DataContext: object dc } &&
            ExecuteMatchingCommand(dc, out _, "CancelCommand", "CancelGenerationCommand"))
        {
            SetStatus("✋ Génération annulée");
            ShowHint("Annulation", "La génération en cours a été annulée.");
            return;
        }

        // À défaut : referme le volet de navigation en mode compact
        if (NavView.IsPaneOpen && NavView.PaneDisplayMode != NavigationViewPaneDisplayMode.Left)
        {
            NavView.IsPaneOpen = false;
        }
    }

    private void OnCtrlBInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AssistantToggleButton.IsChecked = !(AssistantToggleButton.IsChecked == true);
        SetAssistantVisible(AssistantToggleButton.IsChecked == true);
    }

    private void OnCtrlNInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CreateNewFiche();
    }

    private void OnCtrlShiftEInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ContentFrame.Content is Page { DataContext: object dc })
        {
            if (ExecuteMatchingCommand(dc, out bool found,
                    "ExportCommand", "ExportDocxCommand", "ExportPdfCommand", "ExportRtfCommand"))
            {
                SetStatus("📄 Exportation du document…");
                ShowHint("Exportation", "Exportation du document lancée… 📄");
                return;
            }

            ShowHint("Exportation", found
                ? "Exportation indisponible pour le moment."
                : "Générez d'abord un document (Ctrl+G) pour pouvoir l'exporter.");
        }
        else
        {
            ShowHint("Exportation", "Aucun document à exporter sur cette page.");
        }
    }

    private void OnCtrlShiftWInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _isPreviewVisible = !_isPreviewVisible;

        bool handled = false;
        if (ContentFrame.Content is Page page && page.DataContext is object dc)
        {
            handled = ExecuteMatchingCommand(dc, out _, "TogglePreviewCommand");
        }

        ShowHint("Aperçu", handled
            ? (_isPreviewVisible ? "Volet d'aperçu affiché." : "Volet d'aperçu masqué.")
            : "Le volet d'aperçu n'est pas disponible sur cette page.");
    }

    private void OnCtrlPInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ContentFrame.Content is Page { DataContext: object dc })
        {
            if (ExecuteMatchingCommand(dc, out bool found,
                    "PrintCommand", "ExportPdfCommand", "PrintPdfCommand"))
            {
                SetStatus("🖨️ Préparation de l'impression…");
                ShowHint("Impression", "Préparation de l'impression / du PDF… 🖨️");
                return;
            }

            ShowHint("Impression", found
                ? "Impression indisponible pour le moment."
                : "Générez d'abord un document (Ctrl+G) pour l'imprimer.");
        }
        else
        {
            ShowHint("Impression", "Rien à imprimer sur cette page.");
        }
    }

    private void OnCtrlCommaInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NavigateToTag("SettingsPage");
    }

    private void OnCtrlFInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        FocusHistorySearch();
    }

    private void OnCtrlZInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ContentFrame.Content is Page { DataContext: object dc } &&
            ExecuteMatchingCommand(dc, out _, "UndoCommand", "RestoreLastDeletedCommand", "UndoDeleteCommand"))
        {
            SetStatus("↩️ Action annulée");
            ShowHint("Annulation", "Dernière action annulée. ↩️");
        }
        else
        {
            ShowHint("Annulation", "Rien à annuler pour le moment.");
        }
    }

    private void OnCtrlTabInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CycleNavigation(1);
    }

    private void OnCtrlShiftTabInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CycleNavigation(-1);
    }

    private void CycleNavigation(int delta)
    {
        var items = NavView.MenuItems;
        if (items.Count == 0) return;

        int current = items.IndexOf(NavView.SelectedItem);
        int next = ((current + delta) % items.Count + items.Count) % items.Count;
        NavView.SelectedItem = items[next];
    }

    private void OnCtrlNumberInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        int index = sender.Key switch
        {
            VirtualKey.Number1 => 0,
            VirtualKey.Number2 => 1,
            VirtualKey.Number3 => 2,
            VirtualKey.Number4 => 3,
            _ => -1
        };

        if (index >= 0 && index < NavView.MenuItems.Count)
        {
            NavView.SelectedItem = NavView.MenuItems[index];
        }
    }

    private void OnF1Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowShortcutsDialog();
    }

    // ==========================================================================
    //  Dispatch typé des commandes
    // ==========================================================================
    private static bool ExecuteMatchingCommand(object? viewModel, out bool found, params string[] commandNames)
    {
        found = false;
        if (viewModel is null) return false;

        if (viewModel is FicheFormViewModel fvm)
        {
            if (commandNames.Contains("GenerateFicheCommand") || commandNames.Contains("GenerateCommand"))
            {
                found = true;
                if (fvm.GenerateFicheCommand.CanExecute(null)) { fvm.GenerateFicheCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("CancelGenerationCommand") || commandNames.Contains("CancelCommand"))
            {
                found = true;
                if (fvm.CancelGenerationCommand.CanExecute(null)) { fvm.CancelGenerationCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("ResetFormCommand") || commandNames.Contains("NewFicheCommand"))
            {
                found = true;
                if (fvm.ResetFormCommand.CanExecute(null)) { fvm.ResetFormCommand.Execute(null); return true; }
                return false;
            }
        }
        else if (viewModel is EvaluationViewModel evm)
        {
            if (commandNames.Contains("GenerateEvaluationCommand") || commandNames.Contains("GenerateCommand") || commandNames.Contains("GenerateFicheCommand"))
            {
                found = true;
                if (evm.GenerateEvaluationCommand.CanExecute(null)) { evm.GenerateEvaluationCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("CancelGenerationCommand") || commandNames.Contains("CancelCommand"))
            {
                found = true;
                if (evm.CancelGenerationCommand.CanExecute(null)) { evm.CancelGenerationCommand.Execute(null); return true; }
                return false;
            }
        }
        else if (viewModel is QuizViewModel qvm)
        {
            if (commandNames.Contains("GenerateQuizCommand") || commandNames.Contains("GenerateCommand") || commandNames.Contains("GenerateFicheCommand"))
            {
                found = true;
                if (qvm.GenerateQuizCommand.CanExecute(null)) { qvm.GenerateQuizCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("CancelGenerationCommand") || commandNames.Contains("CancelCommand"))
            {
                found = true;
                if (qvm.CancelGenerationCommand.CanExecute(null)) { qvm.CancelGenerationCommand.Execute(null); return true; }
                return false;
            }
        }
        else if (viewModel is HistoryViewModel)
        {
            // History commands handled directly on page
        }

        var resultVm = App.Services.GetService<ResultViewModel>();
        if (resultVm != null && resultVm.HasDocument)
        {
            if (commandNames.Contains("ExportPdfCommand") || commandNames.Contains("ExportCommand"))
            {
                found = true;
                if (resultVm.ExportPdfCommand.CanExecute(null)) { resultVm.ExportPdfCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("ExportDocxCommand"))
            {
                found = true;
                if (resultVm.ExportDocxCommand.CanExecute(null)) { resultVm.ExportDocxCommand.Execute(null); return true; }
                return false;
            }
            if (commandNames.Contains("UndoCommand") && resultVm.CanUndo)
            {
                found = true;
                resultVm.Undo();
                return true;
            }
        }

        return false;
    }

    // ==========================================================================
    //  Astuces contextuelles & barre d'état
    // ==========================================================================
    private void ShowHint(string title, string message)
    {
        ShellHintTip.Title = title;
        ShellHintTip.Subtitle = message;
        ShellHintTip.Target = AiProviderChip;
        ShellHintTip.IsOpen = true;

        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private void SetStatus(string message) => StatusText.Text = message;

    // ==========================================================================
    //  Boîtes de dialogue : raccourcis & à propos
    // ==========================================================================
    private static readonly (string Keys, string Description)[] ShortcutList =
    {
        ("Ctrl + G", "Générer la fiche, l'évaluation ou le quiz"),
        ("Échap", "Annuler la génération en cours"),
        ("Ctrl + B", "Afficher ou masquer l'Assistant IA"),
        ("Ctrl + N", "Créer une nouvelle fiche"),
        ("Ctrl + Maj + E", "Exporter le document (Word, PDF…)"),
        ("Ctrl + Maj + W", "Afficher ou masquer le volet d'aperçu"),
        ("Ctrl + P", "Imprimer ou exporter en PDF"),
        ("Ctrl + F", "Rechercher dans l'historique"),
        ("Ctrl + Z", "Annuler la dernière action"),
        ("Ctrl + Tab", "Passer à la section suivante"),
        ("Ctrl + Maj + Tab", "Revenir à la section précédente"),
        ("Ctrl + 1 à 4", "Accéder directement à une section"),
        ("Ctrl + ,", "Ouvrir les Paramètres"),
        ("F1", "Afficher cette aide"),
    };

    private async void ShowShortcutsDialog()
    {
        try
        {
            var dialog = await CreateShortcutsDialogAsync();
            if (dialog != null)
            {
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Affichage de la boîte de dialogue des raccourcis impossible.");
        }
    }

    private async Task<ContentDialog?> CreateShortcutsDialogAsync()
    {
        var xamlRoot = await EnsureXamlRootAsync();
        if (xamlRoot == null) return null;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Raccourcis clavier",
            PrimaryButtonText = "Fermer",
            DefaultButton = ContentDialogButton.Primary,
            MaxWidth = 600
        };

        var stack = new StackPanel { Spacing = 12 };

        foreach (var (keys, description) in ShortcutList)
        {
            var shortcutItem = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            
            var keysBlock = new TextBlock
            {
                Text = keys,
                FontFamily = new FontFamily("Segoe UI Mono"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Width = 120,
                TextAlignment = TextAlignment.Right
            };
            
            var descBlock = new TextBlock
            {
                Text = description,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            shortcutItem.Children.Add(keysBlock);
            shortcutItem.Children.Add(descBlock);
            stack.Children.Add(shortcutItem);
        }

        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            MaxHeight = 400
        };

        dialog.Content = scroll;
        return dialog;
    }

    private async void ShowAboutDialog()
    {
        try
        {
            var xamlRoot = await EnsureXamlRootAsync();
            if (xamlRoot == null) return;

            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Dev";
            
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = "À propos de PROFstudio",
                Content = new StackPanel
                {
                    Spacing = 16,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                new Border
                                {
                                    Width = 48,
                                    Height = 48,
                                    CornerRadius = new CornerRadius(12),
                                    Background = (SolidColorBrush)Application.Current.Resources["PROFstudioLogoBrush"],
                                    Child = new TextBlock
                                    {
                                        Text = "📘",
                                        FontSize = 24,
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        VerticalAlignment = VerticalAlignment.Center
                                    }
                                },
                                new StackPanel
                                {
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Children =
                                    {
                                        new TextBlock
                                        {
                                            Text = "PROFstudio",
                                            FontSize = 20,
                                            FontWeight = FontWeights.SemiBold
                                        },
                                        new TextBlock
                                        {
                                            Text = "Générateur de fiches pédagogiques intelligent",
                                            FontSize = 14,
                                            Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                                        }
                                    }
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = $"Version : {version}",
                            FontSize = 14
                        },
                        new TextBlock
                        {
                            Text = "PROFstudio aide les enseignants à créer rapidement des fiches pédagogiques, des évaluations et des quiz avec l'aide de l'IA.",
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = "© 2026 PROFstudio. Tous droits réservés.",
                            FontSize = 12,
                            Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        }
                    }
                },
                PrimaryButtonText = "Fermer",
                DefaultButton = ContentDialogButton.Primary
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Affichage de la boîte de dialogue À propos impossible.");
        }
    }

    private void NotificationAction_Click(object sender, RoutedEventArgs e)
    {
        NavigateToTag("SettingsPage");
    }

    private void NotificationDismiss_Click(object sender, RoutedEventArgs e)
    {
        NotificationActionButton.Visibility = Visibility.Collapsed;
        NotificationDismissButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "Prêt pour une nouvelle séance";
        NotificationIcon.Text = "✨";
    }
}

