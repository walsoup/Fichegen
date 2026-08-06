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
    private bool _suppressStartupToggle;

    public SettingsPage()
    {
        // Adapter ici si votre conteneur DI diffère (ex. App.Services.GetRequiredService<...>()).
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
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

        ApplyAppTheme();
        await InitializePreviewAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
        var map = new Dictionary<SelectorBarItem, UIElement>
        {
            { TabGeneral, PanelGeneral },
            { TabAi, PanelAi },
            { TabFolders, PanelFolders },
            { TabStyles, PanelStyles },
            { TabPrivacy, PanelPrivacy },
            { TabPrompts, PanelPrompts },
        };

        foreach (var (tab, panel) in map)
            panel.Visibility = ReferenceEquals(tab, selected) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ManageFolders_Click(object sender, RoutedEventArgs e)
        => SettingsTabs.SelectedItem = TabFolders;

    // ─────────────────────────────────────────────
    //  Thème instantané
    // ─────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Theme))
            ApplyAppTheme();
    }

    private void ApplyAppTheme()
    {
        if (((App)App.Current).MainWindow?.Content is not FrameworkElement root) return;
        root.RequestedTheme = ViewModel.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

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
        try
        {
            await StylePreviewWebView.EnsureCoreWebView2Async();
            _webViewReady = true;
            RefreshPreview();
        }
        catch (Exception)
        {
            StylePreviewWebView.Visibility = Visibility.Collapsed;
            WebViewFallback.Visibility = Visibility.Visible;
        }
    }

    private void OnPreviewRefreshRequested(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(RefreshPreview);

    private void RefreshPreview_Click(object sender, RoutedEventArgs e) => RefreshPreview();

    private void DarkPreviewToggle_Toggled(object sender, RoutedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        if (!_webViewReady || StylePreviewWebView.CoreWebView2 is null) return;

        try
        {
            StylePreviewWebView.CoreWebView2.NavigateToString(
                ViewModel.BuildPreviewHtml(DarkPreviewToggle.IsOn));
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
            Content = "Thème, routage IA, températures, styles et modèles de prompts seront rétablis aux valeurs d'usine. " +
                      "Vos clés d'API (Coffre Windows), vos dossiers et votre historique de fiches sont conservés.",
            PrimaryButtonText = "Réinitialiser",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ResetToDefaultsAsync();
            ApplyAppTheme();
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