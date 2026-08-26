using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
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
    }

    // ───────────────────────── Navigation de l'assistant ─────────────────────────

    private void UpdateStepUi()
    {
        Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

        StepIndicatorText.Text = string.Format(FicheGen.App.Services.L10n.Get("FRD_StepFormat"), _currentStep, 3);
        StepProgress.Value = _currentStep;

        SecondaryButtonText = _currentStep > 1 ? "◄ Précédent" : string.Empty;
        PrimaryButtonText = _currentStep switch
        {
            1 => "Commencer",
            2 => "Continuer",
            _ => "Terminer"
        };
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

    private void ProviderRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderRadioButtons == null || ApiKeySection == null || GetKeyHyperlink == null || KeyOptionalNote == null)
        {
            return;
        }

        switch (ProviderRadioButtons.SelectedIndex)
        {
            case 0: // Service Cloud PROFstudio (Recommandé)
                ApiKeySection.Visibility = Visibility.Collapsed;
                KeyOptionalNote.Text = "Prêt immédiatement : aucun réglage ni clé d'API requise. Vous pouvez vous connecter à votre compte enseignant à tout moment.";
                KeyOptionalNote.Visibility = Visibility.Visible;
                break;

            case 1: // Google AI Studio
                ApiKeySection.Visibility = Visibility.Visible;
                KeyOptionalNote.Visibility = Visibility.Visible;
                GetKeyHyperlink.Content = FicheGen.App.Services.L10n.Get("FRD_GetKeyLink.Content");
                GetKeyHyperlink.NavigateUri = new Uri("https://aistudio.google.com/app/apikey");
                break;

            case 2: // OpenAI
                ApiKeySection.Visibility = Visibility.Visible;
                KeyOptionalNote.Visibility = Visibility.Visible;
                GetKeyHyperlink.Content = FicheGen.App.Services.L10n.Get("FRD_GetKeyLinkOpenAi");
                GetKeyHyperlink.NavigateUri = new Uri("https://platform.openai.com/api-keys");
                break;

            case 3: // Proxy local / Ollama — aucune clé requise pour l'essentiel
                ApiKeySection.Visibility = Visibility.Collapsed;
                KeyOptionalNote.Text = FicheGen.App.Services.L10n.Get("FRD_LocalKeyNote");
                KeyOptionalNote.Visibility = Visibility.Visible;
                break;
        }
    }
}
