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
    // UISettings (et non AccessibilitySettings) : l'abonnement à
    // AccessibilitySettings.HighContrastChanged lève COMException 0x80070490
    // dans une application de bureau WinUI 3 non packagée (pas de core window UWP).
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();

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
        // ColorValuesChanged couvre le basculement contraste élevé ; l'événement
        // arrive sur un thread d'arrière-plan, donc repasse par le DispatcherQueue.
        try
        {
            _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateCaptionButtonColors);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not subscribe to high-contrast changes.");
        }

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
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
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
                        catch (Exception ex)
                        {
                            // Historique corrompu/partial : on ouvre en HTML seul, mais on trace.
                            Log.Warning(ex, "Désérialisation du document historique impossible (id {Id}).", item.Id);
                        }
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

                // Vol de la carte d'historique vers l'aperçu (animation connectée).
                DispatcherQueue.TryEnqueue(
                    () => (ContentFrame.Content as ICreationPage)?.RunIncomingDocumentAnimation());
            };
        }

        var resultViewModel = App.Services.GetService<ResultViewModel>();
        if (resultViewModel != null)
        {
            // Voyant de la barre de titre : pulsation pendant une génération,
            // et célébration de la toute première fiche générée.
            resultViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ResultViewModel.IsBusy))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateAiPulse(resultViewModel.IsBusy);
                        if (!resultViewModel.IsBusy) CelebrateFirstGeneration(resultViewModel);
                    });
                }
            };

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

        try
        {
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

            // Première exécution & transparence RGPD (avant tout autre dialogue)
            await RunFirstRunFlowAsync(xamlRoot);

            // Vérification du composant WebView2 (non bloquant : bouton « Plus tard » par défaut)
            var checker = new WebView2RuntimeChecker();
            if (!checker.IsWebView2Available())
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = Services.L10n.Get("Dialog_WebView2Missing_Title"),
                    Content = Services.L10n.Get("Dialog_WebView2Missing_Message"),
                    PrimaryButtonText = Services.L10n.Get("Dialog_WebView2Missing_Primary"),
                    CloseButtonText = Services.L10n.Get("Dialog_WebView2Missing_Close"),
                    DefaultButton = ContentDialogButton.Close
                };

                var res = await dialog.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    var uri = new Uri(checker.GetDownloadUrl());
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Échec de la séquence d'activation initiale.");
            ShowHint(Services.L10n.Get("Hint_PartialStart_Title"),
                Services.L10n.Get("Hint_PartialStart_Message"));
        }
    }

    private async Task RunFirstRunFlowAsync(XamlRoot xamlRoot)
    {
        var settingsStore = App.Services.GetRequiredService<ISettingsStore>();
        var settings = settingsStore.GetSettings<AppSettings>();
        if (settings.IsFirstRunCompleted) return;

        var firstRunDlg = new FirstRunDialog
        {
            XamlRoot = xamlRoot
        };

        var res = await firstRunDlg.ShowAsync();
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
        }

        // Persisté après application des choix — si l'écriture échoue, le flag
        // n'est pas persisté et la boîte de première exécution sera reposée.
        settings.IsFirstRunCompleted = true;
        await settingsStore.SaveSettingsAsync(settings);

        if (res == ContentDialogResult.Primary)
        {
            ShowHint(Services.L10n.Get("Hint_Welcome_Title"),
                Services.L10n.Get("Hint_Welcome_Message"));
        }
        RefreshAiChip();
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
            "FichePage" => Services.L10n.Get("Section_Fiche"),
            "EvaluationPage" => Services.L10n.Get("Section_Evaluation"),
            "QuizPage" => Services.L10n.Get("Section_Quiz"),
            "HistoryPage" => Services.L10n.Get("Section_History"),
            "SettingsPage" => Services.L10n.Get("Section_Settings"),
            _ => Services.L10n.Get("Section_Fiche")
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

    public void NavigateToTag(string tag)
    {        if (tag == "SettingsPage")
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

        SetStatus(Services.L10n.Get("Status_SearchHistory"));
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
        SetStatus(Services.L10n.Get("Status_NewFiche"));

        if (ContentFrame.Content is Page { DataContext: object dc } &&
            ExecuteMatchingCommand(dc, out _, "ResetFormCommand", "NewFicheCommand"))
        {
            ShowHint(Services.L10n.Get("Hint_NewFiche_Title"), Services.L10n.Get("Hint_NewFiche_Message"));
        }
    }

    private void FocusHistorySearch()
    {
        if (ContentFrame.Content is HistoryPage hp)
        {
            hp.FocusSearchBox();
            SetStatus(Services.L10n.Get("Status_SearchHistory"));
            return;
        }

        void handler(object? s, NavigationEventArgs e)
        {
            ContentFrame.Navigated -= handler;
            if (ContentFrame.Content is HistoryPage h) h.FocusSearchBox();
        }
        ContentFrame.Navigated += handler;
        NavigateToTag("HistoryPage");
        SetStatus(Services.L10n.Get("Status_SearchHistory"));
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

    /// <summary>
    /// Point d'entrée public pour les Paramètres : applique le thème clair / sombre /
    /// système au shell et le persiste dans l'état de la coque (source de vérité unique).
    /// </summary>
    public void ApplyShellTheme(string theme) => ApplyTheme(theme);

    /// <summary>Thème actuellement appliqué au shell (« Light » / « Dark » / « System »).</summary>
    public string CurrentShellTheme => _shellState.Theme;

    /// <summary>
    /// Synchronise la bascule Assistant de la barre de titre avec l'état réellement
    /// appliqué au volet (les pages peuvent modifier la visibilité de leur côté) —
    /// sinon le volet « réapparaît » à la navigation suivante avec un état périmé.
    /// </summary>
    public void SyncAssistantToggle(bool isVisible)
    {
        AssistantToggleButton.IsChecked = isVisible;
        _shellState.IsAssistantVisible = isVisible;
        SaveShellState();
    }

    /// <summary>
    /// État contraste élevé lu depuis le registre (SPI_GETHIGHCONTRACT) :
    /// fiable dans une application non packagée, contrairement à
    /// AccessibilitySettings dont l'abonnement aux événements échoue.
    /// </summary>
    private static bool IsHighContrastEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\HighContrast");
            var raw = key?.GetValue("Flags");
            var flags = raw switch
            {
                int i => i,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 0
            };
            // HCF_HIGHCONTRASTON = 0x00000001
            return (flags & 0x1) != 0;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateCaptionButtonColors()
    {
        try
        {
            var tb = _appWindow.TitleBar;
            bool hc = IsHighContrastEnabled();            bool dark = RootGrid.ActualTheme == ElementTheme.Dark;

            tb.BackgroundColor = Colors.Transparent;
            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.InactiveBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (hc)
            {
                var windowText = GetSystemColorResource("SystemColorWindowTextColor", Colors.White);
                var highlight = GetSystemColorResource("SystemColorHighlightColor", Windows.UI.Color.FromArgb(255, 0, 120, 215));

                tb.ForegroundColor = windowText;
                tb.ButtonForegroundColor = windowText;
                tb.InactiveForegroundColor = windowText;
                tb.ButtonInactiveForegroundColor = windowText;
                tb.ButtonHoverForegroundColor = windowText;
                tb.ButtonHoverBackgroundColor = highlight;
                tb.ButtonPressedForegroundColor = windowText;
                tb.ButtonPressedBackgroundColor = highlight;
                return;
            }

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

    private static Windows.UI.Color GetSystemColorResource(string key, Windows.UI.Color fallback) =>
        Application.Current.Resources[key] is SolidColorBrush brush ? brush.Color : fallback;

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
    private bool _aiPulseStarted;

    private const string CelebratedFirstDocKey = "FicheGen.CelebratedFirstDoc";

    /// <summary>Un seul moment de félicitations, à la toute première fiche générée.</summary>
    private void CelebrateFirstGeneration(ResultViewModel vm)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(vm.CurrentHtml)) return;

            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (values.ContainsKey(CelebratedFirstDocKey)) return;
            values[CelebratedFirstDocKey] = true;

            ShowHint(Services.L10n.Get("Hint_FirstDoc_Title"),
                Services.L10n.Get("Hint_FirstDoc_Message"));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Célébration de première génération ignorée.");
        }
    }

    /// <summary>Fait respirer le voyant IA de la barre de titre pendant une génération.</summary>
    private void UpdateAiPulse(bool busy)
    {
        try
        {
            if (busy && Services.UiMotion.Enabled)
            {
                if (_aiPulseStarted) return;
                if (RootGrid.Resources["AiPulseStoryboard"] is Microsoft.UI.Xaml.Media.Animation.Storyboard sb)
                {
                    sb.Begin();
                    _aiPulseStarted = true;
                }
            }
            else
            {
                if (_aiPulseStarted && RootGrid.Resources["AiPulseStoryboard"] is Microsoft.UI.Xaml.Media.Animation.Storyboard sb)
                {
                    sb.Stop();
                }
                _aiPulseStarted = false;
                AiStatusGlyph.Opacity = 1;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Pulsation du voyant IA non appliquée.");
        }
    }

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
            var name = string.IsNullOrWhiteSpace(settings.Defaults.TeacherName) ? Services.L10n.Get("Profile_DefaultName") : settings.Defaults.TeacherName;
            var school = string.IsNullOrWhiteSpace(settings.Defaults.SchoolName) ? Services.L10n.Get("Profile_DefaultSchool") : settings.Defaults.SchoolName;

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

    // ==========================================================================
    //  Raccourcis clavier — dispatchers
    // ==========================================================================
    private void OnCtrlGInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        // Les pages de création gèrent la validation + le focus sur le champ manquant.
        if (ContentFrame.Content is ICreationPage creationPage)
        {
            if (creationPage.TryStartGeneration())
            {
                SetStatus(Services.L10n.Get("Status_Generating"));
            }
            return;
        }

        ShowHint(Services.L10n.Get("Hint_OpenCreationPage_Title"), Services.L10n.Get("Hint_OpenCreationPage_Message"));
    }

    private void OnEscInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        // La palette de commandes est prioritaire : Échap la referme d'abord.
        if (CommandPaletteOverlay.IsOpen)
        {
            CommandPaletteOverlay.Close();
            return;
        }

        ShellHintTip.IsOpen = false;

        if (ContentFrame.Content is Page { DataContext: object dc } &&
            ExecuteMatchingCommand(dc, out _, "CancelCommand", "CancelGenerationCommand"))
        {
            SetStatus(Services.L10n.Get("Status_Cancelled"));
            ShowHint(Services.L10n.Get("Hint_Undo_Title"), Services.L10n.Get("Hint_Cancel_Message"));
            return;
        }

        // En pleine saisie, Échap ne doit pas déclencher d'actions globales.
        if (IsFocusInEditableField())
        {
            args.Handled = false;
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

    // ==========================================================================
    //  Palette de commandes (Ctrl+K)
    // ==========================================================================
    private void OnCtrlKInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (CommandPaletteOverlay.IsOpen) CommandPaletteOverlay.Close();
        else OpenCommandPalette();
    }

    public void OpenCommandPalette()
    {
        var L = Services.L10n.Get;
        var actions = new List<CommandPalette.CommandAction>
        {
            new(L("Palette_GotoFiche"), L("Palette_GotoFiche_Sub"), "\uE8A5", "Ctrl+1",
                () => NavigateToTag("FichePage")),
            new(L("Palette_GotoEvaluation"), L("Palette_GotoEvaluation_Sub"), "\uE70F", "Ctrl+2",
                () => NavigateToTag("EvaluationPage")),
            new(L("Palette_GotoQuiz"), L("Palette_GotoQuiz_Sub"), "\uE9D5", "Ctrl+3",
                () => NavigateToTag("QuizPage")),
            new(L("Palette_GotoHistory"), L("Palette_GotoHistory_Sub"), "\uE81C", "Ctrl+4",
                () => NavigateToTag("HistoryPage")),

            new(L("Palette_NewFiche"), L("Palette_NewFiche_Sub"), "\uE710", "Ctrl+N", CreateNewFiche),
            new(L("Palette_ToggleAssistant"), L("Palette_ToggleAssistant_Sub"), "\uE99A", "Ctrl+B",
                () =>
                {
                    AssistantToggleButton.IsChecked = AssistantToggleButton.IsChecked != true;
                    SetAssistantVisible(AssistantToggleButton.IsChecked == true);
                }),
            new(L("Palette_ChangeTheme"), L("Palette_ChangeTheme_Sub"), "\uE793", "", CycleTheme),
            new(L("Palette_SearchDocs"), L("Palette_SearchDocs_Sub"), "\uE721", "Ctrl+F",
                FocusHistorySearch),
            new(L("Palette_OpenSettings"), L("Palette_OpenSettings_Sub"), "\uE713", "Ctrl+,",
                () => NavigateToTag("SettingsPage")),
            new(L("Palette_Shortcuts"), L("Palette_Shortcuts_Sub"), "\uE765", "F1", ShowShortcutsDialog),
        };

        if (ContentFrame.Content is ICreationPage creationPage)
        {
            actions.Insert(4, new CommandPalette.CommandAction(
                L("Palette_GenerateNow"), L("Palette_GenerateNow_Sub"), "\uE768", "Ctrl+G",
                () => creationPage.TryStartGeneration()));
        }

        var resultVm = App.Services.GetService<ResultViewModel>();

        // Génération en cours : proposition d'annulation en tête de liste.
        if (resultVm is { IsBusy: true })
        {
            actions.Insert(0, new CommandPalette.CommandAction(
                L("Palette_StopGeneration"), L("Palette_StopGeneration_Sub"), "\uE71A", "Échap",
                () =>
                {
                    if (ContentFrame.Content is not Page page) return;
                    switch (page.DataContext)
                    {
                        case FicheFormViewModel f: f.CancelGenerationCommand.Execute(null); break;
                        case EvaluationViewModel ev: ev.CancelGenerationCommand.Execute(null); break;
                        case QuizViewModel q: q.CancelGenerationCommand.Execute(null); break;
                    }
                }));
        }

        if (resultVm is { HasDocument: true })
        {
            actions.Add(new CommandPalette.CommandAction(
                L("Palette_ExportPdf"), L("Palette_ExportPdf_Sub"), "\uEA90", "",
                () => resultVm.ExportPdfCommand.Execute(null)));
            actions.Add(new CommandPalette.CommandAction(
                L("Palette_ExportWord"), L("Palette_ExportWord_Sub"), "\uE8A5", "",
                () => resultVm.ExportDocxCommand.Execute(null)));
            actions.Add(new CommandPalette.CommandAction(
                L("Palette_PrintDoc"), L("Palette_PrintDoc_Sub"), "\uE749", "Ctrl+P",
                () => resultVm.PrintCommand.Execute(null)));
            actions.Add(new CommandPalette.CommandAction(
                resultVm.IsStudentView ? L("Palette_ShowTeacherVersion") : L("Palette_ShowStudentVersion"),
                L("Palette_ToggleView_Sub"), "\uE77B", "",
                () => resultVm.ToggleStudentViewCommand.Execute(null)));
        }

        CommandPaletteOverlay.Open(actions);
    }

    private void CycleTheme()
    {
        var next = _shellState.Theme switch
        {
            "System" => "Light",
            "Light" => "Dark",
            _ => "System"
        };
        ApplyTheme(next);
        SetStatus(Services.L10n.Format("Status_Theme", next switch { "Light" => Services.L10n.Get("Theme_Light"), "Dark" => Services.L10n.Get("Theme_Dark"), _ => Services.L10n.Get("Theme_System") }));
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
                SetStatus(Services.L10n.Get("Status_Exporting"));
                ShowHint(Services.L10n.Get("Hint_Export_Title"), Services.L10n.Get("Hint_Export_Message"));
                return;
            }

            ShowHint(Services.L10n.Get("Hint_Export_Title"), found
                ? Services.L10n.Get("Hint_Unavailable")
                : Services.L10n.Get("Hint_Export_GenerateFirst"));
        }
        else
        {
            ShowHint(Services.L10n.Get("Hint_Export_Title"), Services.L10n.Get("Hint_NothingToExport"));
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

        ShowHint(Services.L10n.Get("Hint_Preview_Title"), handled
            ? (_isPreviewVisible ? Services.L10n.Get("Hint_Preview_Shown") : Services.L10n.Get("Hint_Preview_Hidden"))
            : Services.L10n.Get("Hint_Preview_Unavailable"));
    }

    private void OnCtrlPInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ContentFrame.Content is Page { DataContext: object dc })
        {
            if (ExecuteMatchingCommand(dc, out bool found,
                    "PrintCommand", "ExportPdfCommand", "PrintPdfCommand"))
            {
                SetStatus(Services.L10n.Get("Status_Printing"));
                ShowHint(Services.L10n.Get("Hint_Print_Title"), Services.L10n.Get("Hint_Print_Message"));
                return;
            }

            ShowHint(Services.L10n.Get("Hint_Print_Title"), found
                ? Services.L10n.Get("Hint_Unavailable")
                : Services.L10n.Get("Hint_Print_GenerateFirst"));
        }
        else
        {
            ShowHint(Services.L10n.Get("Hint_Print_Title"), Services.L10n.Get("Hint_NothingToPrint"));
        }
    }

    private void OnCtrlCommaInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NavigateToTag("SettingsPage");
    }

    private void OnCtrlFInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Ctrl+F dans un champ de saisie : on ne vole pas le raccourci d'édition.
        if (IsFocusInEditableField()) return;

        args.Handled = true;
        FocusHistorySearch();
    }

    private void OnCtrlZInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Ctrl+Z dans un champ de saisie : l'annulation de FRAPPE prime sur celle du document.
        if (IsFocusInEditableField()) return;

        args.Handled = true;

        if (ContentFrame.Content is Page { DataContext: object dc } &&
            ExecuteMatchingCommand(dc, out _, "UndoCommand", "RestoreLastDeletedCommand", "UndoDeleteCommand"))
        {
            SetStatus(Services.L10n.Get("Status_Undone"));
            ShowHint(Services.L10n.Get("Hint_Undo_Title"), Services.L10n.Get("Hint_Undo_Message"));
        }
        else
        {
            ShowHint(Services.L10n.Get("Hint_Undo_Title"), Services.L10n.Get("Hint_NothingToUndo"));
        }
    }

    private bool IsFocusInEditableField()
    {
        var xamlRoot = Content?.XamlRoot;
        if (xamlRoot == null) return false;

        return FocusManager.GetFocusedElement(xamlRoot) is TextBox or PasswordBox or RichEditBox;
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

    private void OnCtrlZeroInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        (ContentFrame.Content as ICreationPage)?.ResetPreviewZoom();
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
            if (commandNames.Contains("PrintCommand") || commandNames.Contains("PrintPdfCommand"))
            {
                found = true;
                if (resultVm.PrintCommand.CanExecute(null)) { resultVm.PrintCommand.Execute(null); return true; }
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
    private static (string Keys, string Description)[] BuildShortcutList() => new[]
    {
        ("Ctrl + K", Services.L10n.Get("Shortcut_Palette")),
        ("Ctrl + G", Services.L10n.Get("Shortcut_Generate")),
        ("Ctrl + molette", Services.L10n.Get("Shortcut_WheelZoom")),
        ("Ctrl + 0", Services.L10n.Get("Shortcut_ResetZoom")),
        ("Échap", Services.L10n.Get("Shortcut_Escape")),
        ("Ctrl + B", Services.L10n.Get("Shortcut_Assistant")),
        ("Ctrl + N", Services.L10n.Get("Shortcut_NewFiche")),
        ("Ctrl + Maj + E", Services.L10n.Get("Shortcut_Export")),
        ("Ctrl + Maj + W", Services.L10n.Get("Shortcut_TogglePreview")),
        ("Ctrl + P", Services.L10n.Get("Shortcut_Print")),
        ("Ctrl + F", Services.L10n.Get("Shortcut_Search")),
        ("Ctrl + Z", Services.L10n.Get("Shortcut_Undo")),
        ("Ctrl + Tab", Services.L10n.Get("Shortcut_NextSection")),
        ("Ctrl + Maj + Tab", Services.L10n.Get("Shortcut_PreviousSection")),
        ("Ctrl + 1 à 4", Services.L10n.Get("Shortcut_GotoSection")),
        ("Ctrl + ,", Services.L10n.Get("Shortcut_Settings")),
        ("F1", Services.L10n.Get("Shortcut_Help")),
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
            Title = Services.L10n.Get("Dialog_Shortcuts_Title"),
            PrimaryButtonText = Services.L10n.Get("Dialog_Close"),
            DefaultButton = ContentDialogButton.Primary,
            MaxWidth = 600
        };

        var stack = new StackPanel { Spacing = 12 };

        foreach (var (keys, description) in BuildShortcutList())
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
                Title = Services.L10n.Get("Dialog_About_Title"),
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
                                            Text = Services.L10n.Get("Dialog_About_Tagline"),
                                            FontSize = 14,
                                            Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                                        }
                                    }
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = Services.L10n.Format("Dialog_About_Version", version),
                            FontSize = 14
                        },
                        new TextBlock
                        {
                            Text = Services.L10n.Get("Dialog_About_Description"),
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = Services.L10n.Get("Dialog_About_Copyright"),
                            FontSize = 12,
                            Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        }
                    }
                },
                PrimaryButtonText = Services.L10n.Get("Dialog_Close"),
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
        StatusText.Text = Services.L10n.Get("Status_Ready");
        NotificationIcon.Text = "✨";
    }
}

