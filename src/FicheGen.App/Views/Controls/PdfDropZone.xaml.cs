using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FicheGen.App.Views.Controls;

public sealed partial class PdfDropZone : UserControl
{
    public static readonly DependencyProperty GuideFilePathProperty =
        DependencyProperty.Register(nameof(GuideFilePath), typeof(string), typeof(PdfDropZone), new PropertyMetadata(string.Empty, OnGuideFilePathChanged));

    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(nameof(DisplayText), typeof(string), typeof(PdfDropZone), new PropertyMetadata("Déposez un guide PDF ici"));

    public string GuideFilePath
    {
        get => (string)GetValue(GuideFilePathProperty);
        set => SetValue(GuideFilePathProperty, value);
    }

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public PdfDropZone()
    {
        InitializeComponent();
    }

    private static void OnGuideFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfDropZone zone && e.NewValue is string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                zone.DisplayText = $"Guide chargé : {System.IO.Path.GetFileName(path)}";
            }
            else
            {
                zone.DisplayText = "Déposez un guide PDF ici";
            }
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        DropBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerOnAcrylicFillColorDefaultBrush"];
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerOnAcrylicFillColorDefaultBrush"];

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 0 && items[0].Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                GuideFilePath = items[0].Path;
            }
        }
    }
}
