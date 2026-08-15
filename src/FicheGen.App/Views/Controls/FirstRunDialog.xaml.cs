using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace FicheGen.App.Views.Controls;

public sealed partial class FirstRunDialog : ContentDialog
{
    private int _currentStep = 1;

    public bool TelemetryEnabled => false;
    public string GuidesPath { get; private set; } = string.Empty;
    public string SelectedProviderKey
    {
        get
        {
            return ProviderRadioButtons.SelectedIndex switch
            {
                1 => "openai",
                2 => "proxy",
                _ => "aistudio"
            };
        }
    }
    public string ApiKey => ApiKeyPasswordBox.Password;

    public FirstRunDialog()
    {
        InitializeComponent();
        UpdateStepVisibility();
    }

    private void UpdateStepVisibility()
    {
        Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

        SecondaryButtonText = _currentStep > 1 ? "◄ Précédent" : string.Empty;
        PrimaryButtonText = _currentStep < 3 ? "Suivant ➔" : "Terminer et lancer 🚀";
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_currentStep < 3)
        {
            args.Cancel = true;
            _currentStep++;
            UpdateStepVisibility();
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_currentStep > 1)
        {
            args.Cancel = true;
            _currentStep--;
            UpdateStepVisibility();
        }
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            if (XamlRoot?.Content is FrameworkElement root)
            {
                var hwnd = WindowNative.GetWindowHandle(root);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            StorageFolder folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                GuidesPath = folder.Path;
                GuidesFolderTextBox.Text = folder.Path;
            }
        }
        catch
        {
            // Fail silently or keep default
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

    private void ProviderRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderRadioButtons == null || ApiKeySection == null || GetKeyHyperlink == null)
        {
            return;
        }

        switch (ProviderRadioButtons.SelectedIndex)
        {
            case 0: // Google AI Studio
                ApiKeySection.Visibility = Visibility.Visible;
                GetKeyHyperlink.Content = "Obtenir une clé gratuite Google AI Studio ↗";
                GetKeyHyperlink.NavigateUri = new Uri("https://aistudio.google.com/app/apikey");
                break;
            case 1: // OpenAI
                ApiKeySection.Visibility = Visibility.Visible;
                GetKeyHyperlink.Content = "Obtenir une clé OpenAI ↗";
                GetKeyHyperlink.NavigateUri = new Uri("https://platform.openai.com/api-keys");
                break;
            case 2: // Proxy Local / Ollama
                ApiKeySection.Visibility = Visibility.Collapsed;
                break;
        }
    }
}
