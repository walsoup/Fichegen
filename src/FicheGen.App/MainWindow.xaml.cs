// -----------------------------------------------------------------------------
// FicheGen — Coque principale (Fluent 2)
// Barre de titre personnalisée, persistance de la fenêtre, raccourcis clavier.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FicheGen.App.Services;
using FicheGen.App.ViewModels;
using FicheGen.App.Views;
using FicheGen.App.Views.Controls;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

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
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private string? _lastExportedFilePath;

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

        var authService = App.Services.GetService<FicheGen.Core.Auth.IAuthService>();
        if (authService != null)
        {
            authService.AuthStateChanged += (s, e) =>
            {
                DispatcherQueue.TryEnqueue(RefreshTeacherBadge);
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

                DispatcherQueue.TryEnqueue(
                    () => (ContentFrame.Content as ICreationPage)?.RunIncomingDocumentAnimation());
            };
        }

        var resultViewModel = App.Services.GetService<ResultViewModel>();
        if (resultViewModel != null)
        {
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

        settings.IsFirstRunCompleted = true;
        await settingsStore.SaveSettingsAsync(settings);

        if (res == ContentDialogResult.Primary)
        {
            ShowHint(Services.L10n.Get("Hint_Welcome_Title"),
                Services.L10n.Get("Hint_Welcome_Message"));
        }
        RefreshAiChip();
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

    // ==========================================================================
    //  Astuces contextuelles & barre d'état
    // ==========================================================================
    private void ShowHint(string title, string message)
    {
        ShellHintTip.Title = title;
        ShellHintTip.Subtitle = message;
        ShellHintTip.Target = ThemeButton;
        ShellHintTip.IsOpen = true;

        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private void SetStatus(string message) => StatusText.Text = message;

    public void ShowExportNotification(string kind, string fileName, string path)
    {
        _lastExportedFilePath = path;
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = $"✓ Document {kind} exporté : {fileName}";
            NotificationIcon.Text = "📁";
            NotificationActionButton.Content = "Ouvrir dans l'Explorateur";
            NotificationActionButton.Visibility = Visibility.Visible;
            NotificationDismissButton.Visibility = Visibility.Visible;

            ShowHint(
                "Document exporté avec succès 🎉",
                $"Le fichier « {fileName} » a été enregistré.\nCliquez sur « Ouvrir dans l'Explorateur » pour le retrouver.");
        });
    }

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
        var xamlRoot = RootGrid.XamlRoot ?? Content?.XamlRoot;
        if (xamlRoot == null) xamlRoot = await EnsureXamlRootAsync();
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

    public async void ShowAboutDialog()
    {
        try
        {
            var xamlRoot = RootGrid.XamlRoot ?? Content?.XamlRoot;
            if (xamlRoot == null)
            {
                xamlRoot = await EnsureXamlRootAsync();
            }
            if (xamlRoot == null) return;

            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
            
            var logoBrush = Application.Current.Resources.TryGetValue("PROFstudioLogoBrush", out var lb) && lb is Brush b
                ? b
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212));

            var secondaryBrush = Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var sb) && sb is Brush sBrush
                ? sBrush
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = Services.L10n.Get("Dialog_About_Title") ?? "À propos de PROFstudio",
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
                                    Background = logoBrush,
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
                                            Text = Services.L10n.Get("Dialog_About_Tagline") ?? "Générateur de fiches pédagogiques intelligent",
                                            FontSize = 14,
                                            Foreground = secondaryBrush
                                        }
                                    }
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = Services.L10n.Format("Dialog_About_Version", version),
                            FontSize = 14,
                            FontWeight = FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = Services.L10n.Get("Dialog_About_Description") ?? "PROFstudio aide les enseignants à concevoir rapidement des fiches pédagogiques, des évaluations et des quiz avec l'aide de l'IA.",
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = Services.L10n.Get("Dialog_About_Copyright") ?? "© 2026 PROFstudio. Tous droits réservés.",
                            FontSize = 12,
                            Foreground = secondaryBrush
                        }
                    }
                },
                PrimaryButtonText = Services.L10n.Get("Dialog_Close") ?? "Fermer",
                DefaultButton = ContentDialogButton.Primary
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Affichage de la boîte de dialogue À propos impossible via ContentDialog, utilisation de ShowHint.");
            ShowHint(Services.L10n.Get("Dialog_About_Title") ?? "À propos de PROFstudio", "PROFstudio v1.4 — Conçu pour les enseignants.");
        }
    }

    private void NotificationAction_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastExportedFilePath) && File.Exists(_lastExportedFilePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_lastExportedFilePath}\"",
                    UseShellExecute = true
                });
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Impossible d'ouvrir l'Explorateur sur le fichier exporté.");
            }
        }

        NavigateToTag("SettingsPage");
    }

    private void NotificationDismiss_Click(object sender, RoutedEventArgs e)
    {
        NotificationActionButton.Visibility = Visibility.Collapsed;
        NotificationDismissButton.Visibility = Visibility.Collapsed;
        StatusText.Text = Services.L10n.Get("Status_Ready");
        NotificationIcon.Text = "✨";
        _lastExportedFilePath = null;
    }
}
