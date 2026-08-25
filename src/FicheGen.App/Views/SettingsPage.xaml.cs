using System;
using System.Collections.Generic;
using FicheGen.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.Extensions.DependencyInjection;

namespace FicheGen.App.Views;

/// <summary>
/// Page Paramètres — interface à onglets Fluent 2 (SelectorBar + SettingsCard/SettingsExpander).
/// La logique métier réside dans <see cref="SettingsViewModel"/> ; ce code-behind ne gère que
/// l'interop WinRT (StartupTask, FileOpenPicker, WebView2, thème instantané).
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    private bool _webViewReady;
    private bool _webViewFailed;
    private bool _suppressStartupToggle;

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        DataContext = ViewModel;

        InitializeComponent();

        ViewModel.PreviewRefreshRequested += OnPreviewRefreshRequested;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ─────────────────────────────────────────────
    //  Initialisation
    // ─────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Onglet initial
        SettingsTabs.SelectedItem = TabGeneral;
        SwitchPanels(TabGeneral);

        // Pré-remplissage des PasswordBox (Password n'est pas une DependencyProperty :
        // la liaison de données est volontairement réalisée en code-behind).
        GeminiPasswordBox.Password = ViewModel.GeminiApiKey;
        OpenAiPasswordBox.Password = ViewModel.OpenAiApiKey;
        AnthropicPasswordBox.Password = ViewModel.AnthropicApiKey;
        ProxyPasswordBox.Password = ViewModel.ProxyApiKey;
        VercelPasswordBox.Password = ViewModel.VercelApiKey;

        // État initial du StartupTask (nécessite une identité de package MSIX).
        try
        {
            var task = await StartupTask.GetAsync("FicheGenStartupTask");
            _suppressStartupToggle = true;
            StartupToggle.IsOn = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            _suppressStartupToggle = false;
            ViewModel.StartupStatusText = StartupToggle.IsOn
                ? "Activé — PROFstudio démarre automatiquement avec Windows."
                : "Désactivé — PROFstudio ne démarre qu'à la demande.";
            ViewModel.LaunchAtStartup = StartupToggle.IsOn;
        }
        catch (Exception)
        {
            ViewModel.StartupStatusText = "Disponible uniquement en mode empaqueté (MSIX).";
        }

        // L'aperçu du StyleBuilder s'initialise paresseusement à la première
        // ouverture de l'onglet Styles (voir UpdateStylePreviewVisibility) :
        // initialisé au chargement de la page, hors écran, son HWND se peint
        // à une position périmée et recouvre l'onglet.
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PreviewRefreshRequested -= OnPreviewRefreshRequested;
    }

    // ─────────────────────────────────────────────
    //  Navigation par onglets (SelectorBar)
    // ─────────────────────────────────────────────

    private void SettingsTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        SwitchPanels(SettingsTabs.SelectedItem as SelectorBarItem);
    }

    private void SwitchPanels(SelectorBarItem? selected)
    {
        var isStylesTab = ReferenceEquals(selected, TabStyles);

        if (isStylesTab)
        {
            SettingsScrollViewer.Visibility = Visibility.Collapsed;
            PanelStyles.Visibility = Visibility.Visible;
            FicheGen.App.Services.UiMotion.FadeUp(PanelStyles, 12);
            UpdateStylePreviewVisibility(true);
            return;
        }

        PanelStyles.Visibility = Visibility.Collapsed;
        SettingsScrollViewer.Visibility = Visibility.Visible;
        UpdateStylePreviewVisibility(false);

        var map = new Dictionary<SelectorBarItem, FrameworkElement>
        {
            { TabGeneral, PanelGeneral },
            { TabAi, PanelAi },
            { TabFolders, PanelFolders },
            { TabPrivacy, PanelPrivacy },
            { TabPrompts, PanelPrompts },
        };

        foreach (var (tab, panel) in map)
        {
            var isSelected = ReferenceEquals(tab, selected);
            if (isSelected && panel.Visibility != Visibility.Visible)
            {
                panel.Visibility = Visibility.Visible;
                // Entrée en douceur du panneau nouvellement affiché.
                FicheGen.App.Services.UiMotion.FadeUp(panel, 12);
            }
            else
            {
                panel.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// L'aperçu WebView2 n'est initialisé et affiché QUE lorsque l'onglet Styles est
    /// actif : initialisé hors écran, l'HWND de WebView2 se peint à une position
    /// périmée et recouvre l'onglet d'une surface blanche vide (airspaces HWND).
    /// </summary>
    private bool _isPreviewInitialized;

    private void UpdateStylePreviewVisibility(bool isStylesTabSelected)
    {
        if (StylePreviewWebView is null) return;

        if (!isStylesTabSelected)
        {
            if (WebViewFallback is not null) WebViewFallback.Visibility = Visibility.Collapsed;
            return;
        }

        if (_webViewFailed)
        {
            if (WebViewFallback is not null) WebViewFallback.Visibility = Visibility.Visible;
            return;
        }

        if (!_isPreviewInitialized)
        {
            _ = InitializePreviewAsync();
        }
        else
        {
            RefreshPreview();
        }
    }

    private void ManageFolders_Click(object sender, RoutedEventArgs e)
        => SettingsTabs.SelectedItem = TabFolders;

    // ─────────────────────────────────────────────
    //  Thème instantané
    // ─────────────────────────────────────────────

    // Le thème est appliqué et persisté par MainWindow.ApplyShellTheme, déclenché
    // par SettingsViewModel.OnThemeChanged. La page ne réécrit JAMAIS
    // RootGrid.RequestedTheme elle-même : un réglage « Défaut » écrasait le thème
    // du shell et basculait l'application en sombre à l'ouverture des Paramètres.

    // ─────────────────────────────────────────────
    //  Clés d'API (PasswordBox non-bindable)
    // ─────────────────────────────────────────────

    private void GeminiPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => ViewModel.GeminiApiKey = GeminiPasswordBox.Password;
    private void OpenAiPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => ViewModel.OpenAiApiKey = OpenAiPasswordBox.Password;
    private void AnthropicPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => ViewModel.AnthropicApiKey = AnthropicPasswordBox.Password;
    private void ProxyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => ViewModel.ProxyApiKey = ProxyPasswordBox.Password;
    private void VercelPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => ViewModel.VercelApiKey = VercelPasswordBox.Password;

    private void ClearApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string providerKey }) return;

        switch (providerKey)
        {
            case "aistudio": GeminiPasswordBox.Password = string.Empty; break;
            case "openai": OpenAiPasswordBox.Password = string.Empty; break;
            case "anthropic": AnthropicPasswordBox.Password = string.Empty; break;
            case "proxy": ProxyPasswordBox.Password = string.Empty; break;
            case "vercel": VercelPasswordBox.Password = string.Empty; break;
        }

        ViewModel.ClearApiKey(providerKey);
    }

    // ─────────────────────────────────────────────
    //  Démarrage Windows (StartupTask WinRT)
    // ─────────────────────────────────────────────

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressStartupToggle) return;

        try
        {
            var task = await StartupTask.GetAsync("FicheGenStartupTask");

            if (StartupToggle.IsOn)
            {
                var state = await task.RequestEnableAsync();
                ViewModel.StartupStatusText = state switch
                {
                    StartupTaskState.Enabled => "Activé — PROFstudio démarrera avec Windows.",
                    StartupTaskState.DisabledByPolicy => "Bloqué par la stratégie de groupe de votre établissement.",
                    StartupTaskState.DisabledByUser => "Refusé — réactivez-le via le Gestionnaire des tâches (onglet Démarrage).",
                    _ => "En attente d'approbation par l'utilisateur.",
                };

                if (state != StartupTaskState.Enabled)
                {
                    _suppressStartupToggle = true;
                    StartupToggle.IsOn = false;
                    _suppressStartupToggle = false;
                }
                ViewModel.LaunchAtStartup = state == StartupTaskState.Enabled;
            }
            else
            {
                task.Disable();
                ViewModel.StartupStatusText = "Désactivé — PROFstudio ne démarre qu'à la demande.";
                ViewModel.LaunchAtStartup = false;
            }
        }
        catch (Exception)
        {
            ViewModel.StartupStatusText = "Indisponible en mode non empaqueté — requiert l'identité MSIX.";
            _suppressStartupToggle = true;
            StartupToggle.IsOn = false;
            _suppressStartupToggle = false;
            ViewModel.LaunchAtStartup = false;
        }
    }

    // ─────────────────────────────────────────────
    //  Aperçu WebView2 du StyleBuilder
    // ─────────────────────────────────────────────

    private async System.Threading.Tasks.Task InitializePreviewAsync()
    {
        if (StylePreviewWebView == null) return;

        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(version))
            {
                _webViewFailed = true;
                if (WebViewFallback != null) WebViewFallback.Visibility = Visibility.Visible;
                return;
            }

            await StylePreviewWebView.EnsureCoreWebView2Async();
            _webViewReady = true;
            _isPreviewInitialized = true;
            RefreshPreview();
        }
        catch (Exception)
        {
            _webViewFailed = true;
            if (WebViewFallback != null) WebViewFallback.Visibility = Visibility.Visible;
        }
    }

    private void OnPreviewRefreshRequested(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(RefreshPreview);

    private void RefreshPreview_Click(object sender, RoutedEventArgs e) => RefreshPreview();

    private void PaletteChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string paletteKey })
        {
            ViewModel.ApplyPalette(paletteKey);
        }
    }

    private void RefreshPreview()
    {
        if (!_webViewReady || StylePreviewWebView.CoreWebView2 is null) return;

        try
        {
            StylePreviewWebView.CoreWebView2.NavigateToString(ViewModel.BuildPreviewHtml());
        }
        catch (Exception)
        {
            // Le runtime WebView2 peut être momentanément indisponible ; on ignore silencieusement.
        }
    }

    // ─────────────────────────────────────────────
    //  Générateur de style par IA (sélection PDF)
    // ─────────────────────────────────────────────

    private async void AnalyzeStylePdf_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".pdf");

        if (((App)App.Current).MainWindow is not null)
        {
            var hwnd = WindowNative.GetWindowHandle(((App)App.Current).MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            await ViewModel.AnalyzeStylePdfAsync(file.Name);
    }

    // ─────────────────────────────────────────────
    //  Dialogues de confirmation
    // ─────────────────────────────────────────────

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Vider le cache de la table des matières",
            Content = $"Cette action supprimera définitivement le contenu du cache ({ViewModel.TocCacheSizeText}). " +
                      "Les tables des matières seront régénérées à la prochaine analyse des guides PDF.",
            PrimaryButtonText = "Vider le cache",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.ClearTocCacheAsync();
    }

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Réinitialiser tous les paramètres ?",
            Content = "Thème, routage IA, réglages de créativité, styles et modèles de prompts seront rétablis aux valeurs d'usine. " +
                      "Vos clés d'API (Coffre Windows), vos dossiers et votre historique de fiches sont conservés.",
            PrimaryButtonText = "Réinitialiser",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ResetToDefaultsAsync();
        }
    }

    // ─────────────────────────────────────────────
    //  Raccourci global Ctrl+S
    // ─────────────────────────────────────────────

    private void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SaveSettingsCommand.CanExecute(null))
            ViewModel.SaveSettingsCommand.Execute(null);
        args.Handled = true;
    }
}