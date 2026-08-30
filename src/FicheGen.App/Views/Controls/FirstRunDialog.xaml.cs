using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace FicheGen.App.Views.Controls;

/// <summary>
/// Parcours de première exécution (3 étapes) rédigé pour des enseignants
/// sans bagage technique : vocabulaire courant, progression visible,
/// étapes facultatives clairement identifiées et sortie douce possible à tout moment.
/// </summary>
public sealed partial class FirstRunDialog : ContentDialog
{
    private int _currentStep = 1;

    public bool TelemetryEnabled => false;
    public string GuidesPath { get; private set; } = string.Empty;

    public string SelectedProviderKey =>
        ProviderRadioButtons.SelectedIndex switch
        {
            1 => "aistudio",
            2 => "openai",
            3 => "proxy",
            _ => "cloud"
        };

    public string ApiKey => ApiKeyPasswordBox.Password;

    public FirstRunDialog()
    {
        InitializeComponent();
        UpdateStepUi();
        RefreshCloudAccountStatus();
    }

    // ───────────────────────── Navigation de l'assistant ─────────────────────────

    private void UpdateStepUi()
    {
        Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

        var stepText = string.Format(FicheGen.App.Services.L10n.Get("FRD_StepFormat"), _currentStep, 3);
        StepIndicatorText.Text = stepText;
        StepProgress.Value = _currentStep;

        SecondaryButtonText = _currentStep > 1 ? "◄ Précédent" : string.Empty;
        PrimaryButtonText = _currentStep switch
        {
            1 => "Commencer",
            2 => "Continuer",
            _ => "Terminer"
        };

        // Accessibilité (F25) : annonce du changement d'étape et focus
        if (FrameworkElementAutomationPeer.FromElement(StepIndicatorText) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            switch (_currentStep)
            {
                case 1:
                    Step1Panel.Focus(FocusState.Programmatic);
                    break;
                case 2:
                    GuidesFolderTextBox.Focus(FocusState.Programmatic);
                    break;
                case 3:
                    ProviderRadioButtons.Focus(FocusState.Programmatic);
                    RefreshCloudAccountStatus();
                    break;
            }
        });
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_currentStep < 3)
        {
            args.Cancel = true;
            _currentStep++;
            UpdateStepUi();
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_currentStep > 1)
        {
            args.Cancel = true;
            _currentStep--;
            UpdateStepUi();
        }
    }

    // ───────────────────────── Choix du dossier de documents ─────────────────────────

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            // The handle must come from the Window, not from a FrameworkElement:
            // GetWindowHandle(root) returns a useless handle and the picker throws.
            var mainWindow = App.CurrentMainWindow;
            if (mainWindow != null)
            {
                var hwnd = WindowNative.GetWindowHandle(mainWindow);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            StorageFolder folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                GuidesPath = folder.Path;
                GuidesFolderTextBox.Text = folder.Path;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Sélection du dossier guides (première exécution) impossible.");
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 0 && items[0] is StorageFolder folder)
            {
                GuidesPath = folder.Path;
                GuidesFolderTextBox.Text = folder.Path;
            }
        }
    }

    // ───────────────────────── Choix de l'assistant IA ─────────────────────────

    private void RefreshCloudAccountStatus()
    {
        if (CloudAccountStatusText == null || ConnectAccountButton == null) return;

        var credStore = App.Services.GetService<FicheGen.Core.Abstractions.ICredentialStore>();
        var token = credStore?.Get("supabase_access_token");
        var hasToken = !string.IsNullOrWhiteSpace(token);

        if (hasToken)
        {
            CloudAccountStatusText.Text = "✓ Connecté à votre compte enseignant PROFstudio. La génération en ligne est prête.";
            ConnectAccountButton.Content = "Gérer mon compte enseignant…";
            ConnectAccountButton.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
        }
        else
        {
            CloudAccountStatusText.Text = "Le service en ligne nécessite une connexion à votre compte enseignant pour générer des fiches.";
            ConnectAccountButton.Content = "Se connecter / Créer un compte…";
            ConnectAccountButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }
    }

    private async void ConnectAccountButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AccountDialog
        {
            XamlRoot = this.XamlRoot
        };
        await dlg.ShowAsync();
        RefreshCloudAccountStatus();
    }

    private void ProviderRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderRadioButtons == null || ApiKeySection == null || GetKeyHyperlink == null || KeyOptionalNote == null || CloudAccountSection == null)
        {
            return;
        }

        switch (ProviderRadioButtons.SelectedIndex)
        {
            case 0: // Service Cloud PROFstudio (Recommandé)
                CloudAccountSection.Visibility = Visibility.Visible;
                ApiKeySection.Visibility = Visibility.Collapsed;
                KeyOptionalNote.Text = "Connectez-vous pour utiliser le service en ligne, ou sélectionnez Google AI Studio / Ollama.";
                KeyOptionalNote.Visibility = Visibility.Visible;
                RefreshCloudAccountStatus();
                break;

            case 1: // Google AI Studio
                CloudAccountSection.Visibility = Visibility.Collapsed;
                ApiKeySection.Visibility = Visibility.Visible;
                KeyOptionalNote.Visibility = Visibility.Visible;
                GetKeyHyperlink.Content = FicheGen.App.Services.L10n.Get("FRD_GetKeyLink.Content");
                GetKeyHyperlink.NavigateUri = new Uri("https://aistudio.google.com/app/apikey");
                break;

            case 2: // OpenAI
                CloudAccountSection.Visibility = Visibility.Collapsed;
                ApiKeySection.Visibility = Visibility.Visible;
                KeyOptionalNote.Visibility = Visibility.Visible;
                GetKeyHyperlink.Content = FicheGen.App.Services.L10n.Get("FRD_GetKeyLinkOpenAi");
                GetKeyHyperlink.NavigateUri = new Uri("https://platform.openai.com/api-keys");
                break;

            case 3: // Proxy local / Ollama — aucune clé requise pour l'essentiel
                CloudAccountSection.Visibility = Visibility.Collapsed;
                ApiKeySection.Visibility = Visibility.Collapsed;
                KeyOptionalNote.Text = FicheGen.App.Services.L10n.Get("FRD_LocalKeyNote");
                KeyOptionalNote.Visibility = Visibility.Visible;
                break;
        }
    }
}
