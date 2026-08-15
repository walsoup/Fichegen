using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace FicheGen.App.Views.Controls;

public sealed partial class PdfDropZone : UserControl
{
    public static readonly DependencyProperty GuideFilePathProperty =
        DependencyProperty.Register(
            nameof(GuideFilePath),
            typeof(string),
            typeof(PdfDropZone),
            new PropertyMetadata(string.Empty, OnGuideFilePathChanged));

    public string GuideFilePath
    {
        get => (string)GetValue(GuideFilePathProperty);
        set => SetValue(GuideFilePathProperty, value);
    }

    public PdfDropZone()
    {
        InitializeComponent();
        UpdateState();
    }

    private static void OnGuideFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfDropZone zone)
        {
            zone.UpdateState();
        }
    }

    public void SetError(string title, string message)
    {
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        DragOverPanel.Visibility = Visibility.Collapsed;
        LoadedStatePanel.Visibility = Visibility.Collapsed;
        ProcessingPanel.Visibility = Visibility.Collapsed;
        ErrorStatePanel.Visibility = Visibility.Visible;

        ErrorTitleText.Text = title;
        ErrorMessageText.Text = message;
    }

    private void UpdateState()
    {
        var path = GuideFilePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            DragOverPanel.Visibility = Visibility.Collapsed;
            LoadedStatePanel.Visibility = Visibility.Collapsed;
            ErrorStatePanel.Visibility = Visibility.Collapsed;
            ProcessingPanel.Visibility = Visibility.Collapsed;

            DropBorder.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
            DropBorder.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        }
        else
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            DragOverPanel.Visibility = Visibility.Collapsed;
            LoadedStatePanel.Visibility = Visibility.Visible;
            ErrorStatePanel.Visibility = Visibility.Collapsed;
            ProcessingPanel.Visibility = Visibility.Collapsed;

            FileNameText.Text = Path.GetFileName(path);
            try
            {
                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    var sizeKb = fileInfo.Length / 1024;
                    FileMetaText.Text = sizeKb > 1024
                        ? $"{sizeKb / 1024.0:F1} Mo · Prêt pour l'ancrage pédagogique"
                        : $"{sizeKb} Ko · Prêt pour l'ancrage pédagogique";
                }
                else
                {
                    FileMetaText.Text = "Guide PDF sélectionné";
                }
            }
            catch
            {
                FileMetaText.Text = "Guide PDF sélectionné";
            }
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        DragOverPanel.Visibility = Visibility.Visible;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        LoadedStatePanel.Visibility = Visibility.Collapsed;
        ErrorStatePanel.Visibility = Visibility.Collapsed;

        DropBorder.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        DropBorder.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        UpdateState();
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        UpdateState();

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0)
                {
                    var item = items[0];
                    if (item.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        GuideFilePath = item.Path;
                    }
                    else
                    {
                        SetError("Format non supporté", "Seuls les fichiers PDF (.pdf) sont acceptés pour les guides.");
                    }
                }
            }
            catch (Exception ex)
            {
                SetError("Lecture impossible", "Le fichier n'a pas pu être lu. Vérifiez qu'il n'est pas verrouillé.");
                Serilog.Log.Warning(ex, "Erreur lors du dépôt de fichier PDF.");
            }
        }
    }

    private void OnClearFileClicked(object sender, RoutedEventArgs e)
    {
        GuideFilePath = string.Empty;
        UpdateState();
    }
}
