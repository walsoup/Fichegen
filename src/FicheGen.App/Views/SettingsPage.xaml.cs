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
public sealed partial class SettingsPage : Page, ILocalizablePage
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

    // ───────────��─────────────────────────────────
    //  Initialisation
    // ─────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshLocalizedStrings();

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

    public void RefreshLocalizedStrings()
    {
        var L = Services.L10n.Get;

        // En-tête & Onglets
        PageTitle.Text = L("SP_Title.Text", "Paramètres");
        PageSubtitle.Text = L("SP_Subtitle.Text", "Configurez PROFstudio : apparence, intelligence artificielle, styles de documents et confidentialité.");
        TabGeneral.Text = L("SP_TabGeneral.Text", "Général");
        TabAi.Text = L("SP_TabAi.Text", "IA & Routage");
        TabFolders.Text = L("SP_TabFolders.Text", "Dossiers");
        TabStyles.Text = L("SP_TabStyles.Text", "Styles");
        TabPrivacy.Text = L("SP_TabPrivacy.Text", "Confidentialité");
        TabPrompts.Text = L("SP_TabPrompts.Text", "Prompts & Raccourcis");

        // Onglet Général
        AccountHeader.Text = L("SP_AccountHeader.Text", "Compte & Accès Enseignant");
        AccountCard.Header = L("SP_AccountCard.Header", "Mon compte enseignant PROFstudio");
        AccountCard.Description = L("SP_AccountCard.Description", "Connectez-vous avec votre compte enseignant pour activer les modèles IA Cloud et synchroniser vos fiches.");
        AccountConnectBtn.Content = L("SP_AccountConnectBtn.Content", "Connexion / Créer un compte…");
        AccountProfileBtn.Content = L("SP_AccountProfileBtn.Content", "Mon profil & Quota…");

        ProfileHeader.Text = L("SP_ProfileHeader.Text", "Profil de l'enseignant·e");
        TeacherNameCard.Header = L("SP_TeacherName.Header", "Nom / Identité");
        TeacherNameCard.Description = L("SP_TeacherName.Description", "Affiché dans l'en-tête de l'application et sur vos fiches.");
        TeacherNameBox.PlaceholderText = L("SP_TeacherNameBox.PlaceholderText", "ex. M. Dupont / Mme Martin");
        SchoolNameCard.Header = L("SP_SchoolName.Header", "École / Établissement");
        SchoolNameCard.Description = L("SP_SchoolName.Description", "Nom de l'établissement imprimé sur les documents générés.");
        SchoolNameBox.PlaceholderText = L("SP_SchoolNameBox.PlaceholderText", "ex. École Louise-Michel");

        UiHeader.Text = L("SP_UiHeader.Text", "Personnalisation de l'interface");
        AppThemeCard.Header = L("SP_AppTheme.Header", "Thème de l'application");
        AppThemeCard.Description = L("SP_AppTheme.Description", "Appliqué immédiatement à toute l'interface PROFstudio.");
        AccentColorCard.Header = L("SP_AccentColor.Header", "Couleur d'accentuation");
        AccentColorCard.Description = L("SP_AccentColor.Description", "Teinte de marque PROFstudio — appliquée immédiatement à toute l'interface.");
        LanguageCard.Header = L("SP_Language.Header", "Langue de l'interface");
        LanguageCard.Description = L("SP_Language.Description", "Sélectionnez la langue de l'application (Français, English, العربية).");
        StreamingCard.Header = L("SP_Streaming.Header", "Affichage du texte en direct (Streaming)");
        StreamingCard.Description = L("SP_Streaming.Description", "Affiche le document au fur et à mesure de sa rédaction par l'IA.");

        PedagogicalHeader.Text = L("SP_PedagogicalHeader.Text", "Préférences pédagogiques");
        DefaultLevelCard.Header = L("SP_DefaultLevel.Header", "Niveau scolaire par défaut");
        DefaultLevelCard.Description = L("SP_DefaultLevel.Description", "Présélectionné automatiquement à chaque nouvelle fiche.");
        DefaultSubjectCard.Header = L("SP_DefaultSubject.Header", "Matière par défaut");
        DefaultSubjectCard.Description = L("SP_DefaultSubject.Description", "Champ libre — choisissez dans la liste ou saisissez votre matière.");

        StartupHeader.Text = L("SP_StartupHeader.Text", "Démarrage de Windows");
        LaunchAtStartupCard.Header = L("SP_LaunchAtStartup.Header", "Lancer PROFstudio au démarrage de Windows");
        AboutHeader.Text = L("SP_AboutHeader.Text", "À propos de l'application");
        AboutCard.Header = L("SP_AboutCard.Header", "À propos de PROFstudio");
        AboutCard.Description = L("SP_AboutCard.Description", "Version 1.4 — Conçu pour les enseignants. Génération pédagogique locale et sécurisée.");
        AboutBtn.Content = L("SP_AboutBtn.Content", "Afficher les informations…");

        SaveLabel.Text = L("SP_SaveLabel.Text", L("SP_SaveButton.Content", "Enregistrer les paramètres"));

        ViewModel.RefreshLocalizedOptions();
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
                      "Vos clés d'API, vos dossiers et votre historique de fiches sont conservés.",
            PrimaryButtonText = "Réinitialiser",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ResetToDefaultsAsync();
        }
    }

    private async void OpenAccountDialog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FicheGen.App.Views.Controls.AccountDialog
        {
            XamlRoot = this.XamlRoot
        };
        await dlg.ShowAsync();
    }

    public static double VisibleToOpacity(bool isVis) => isVis ? 1.0 : 0.4;
    public static Visibility BoolToVisibility(bool val) => val ? Visibility.Visible : Visibility.Collapsed;

    private StylePresetItem? _draggedPreset;

    private void ThemeCard_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var item = (sender as FrameworkElement)?.DataContext as StylePresetItem
                ?? (sender as FrameworkElement)?.Tag as StylePresetItem;
        if (item != null)
        {
            _draggedPreset = item;
            args.Data.SetText(item.Id);
            args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        }
    }

    private void ThemeCard_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        if (sender is Border border)
        {
            border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            border.BorderThickness = new Thickness(2);
        }
    }

    private void ThemeCard_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
            border.BorderThickness = new Thickness(1);
        }
    }

    private void ThemeCard_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
            border.BorderThickness = new Thickness(1);
        }

        var targetItem = (sender as FrameworkElement)?.DataContext as StylePresetItem
                      ?? (sender as FrameworkElement)?.Tag as StylePresetItem;
        if (_draggedPreset != null && targetItem != null && !ReferenceEquals(_draggedPreset, targetItem))
        {
            ViewModel.ReorderTheme(_draggedPreset, targetItem);
        }
        _draggedPreset = null;
    }

    private void ThemeCard_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var cur = dep;
            while (cur != null && !ReferenceEquals(cur, sender))
            {
                if (cur is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                {
                    return;
                }
                cur = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cur);
            }
        }

        var item = (sender as FrameworkElement)?.DataContext as StylePresetItem
                ?? (sender as FrameworkElement)?.Tag as StylePresetItem;
        if (item != null)
        {
            ViewModel.SelectedPreset = item;
        }
    }

    private void SetAsDefaultTheme_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.Tag as StylePresetItem
                ?? (sender as FrameworkElement)?.DataContext as StylePresetItem;
        if (item != null) ViewModel.SetAsDefaultThemeCommand.Execute(item);
    }

    private void EmojiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string emoji })
        {
            ViewModel.SelectBuilderIcon(emoji);
        }
    }

    private void MoveThemeLeft_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.Tag as StylePresetItem ?? (sender as FrameworkElement)?.DataContext as StylePresetItem;
        if (item != null) ViewModel.MoveThemeLeftCommand.Execute(item);
    }

    private void MoveThemeRight_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.Tag as StylePresetItem ?? (sender as FrameworkElement)?.DataContext as StylePresetItem;
        if (item != null) ViewModel.MoveThemeRightCommand.Execute(item);
    }

    private void DuplicateTheme_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.Tag as StylePresetItem ?? (sender as FrameworkElement)?.DataContext as StylePresetItem;
        if (item != null) ViewModel.DuplicateThemeCommand.Execute(item);
    }

    private void ToggleThemeVisibility_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.Tag as StylePresetItem ?? (sender as FrameworkElement)?.DataContext as StylePresetItem;
        if (item != null) ViewModel.ToggleThemeVisibilityCommand.Execute(item);
    }

    private void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.Tag as StylePresetItem ?? (sender as FrameworkElement)?.DataContext as StylePresetItem;
        if (item != null) ViewModel.DeleteThemeCommand.Execute(item);
    }

    private void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SaveSettingsCommand.CanExecute(null))
            ViewModel.SaveSettingsCommand.Execute(null);
        args.Handled = true;
    }
}