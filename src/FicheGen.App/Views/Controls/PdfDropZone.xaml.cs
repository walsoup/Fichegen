using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.App.Services;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Toc;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Chapitre sélectionné dans la table des matières du guide (peut être null).</summary>
    public event EventHandler<ToCEntry>? ChapterSelected;

    private CancellationTokenSource? _tocLoadCts;
    private bool _suppressChapterSelection;

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
            zone.LoadChapterListAsync();
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
                        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Services.L10n.Get("PDZ_MetaMb"), sizeKb / 1024.0)
                        : string.Format(System.Globalization.CultureInfo.CurrentCulture, Services.L10n.Get("PDZ_MetaKb"), sizeKb);
                }
                else
                {
                    FileMetaText.Text = Services.L10n.Get("PDZ_FileSelected");
                }
            }
            catch
            {
                FileMetaText.Text = Services.L10n.Get("PDZ_FileSelected");
            }
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            DragOverPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            LoadedStatePanel.Visibility = Visibility.Collapsed;
            ErrorStatePanel.Visibility = Visibility.Collapsed;

            DropBorder.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            DropBorder.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
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
                        var info = new FileInfo(item.Path);
                        if (info.Exists && info.Length > 50 * 1024 * 1024)
                        {
                            SetError("Fichier volumineux", "La taille du guide PDF dépasse la limite de 50 Mo.");
                        }
                        else
                        {
                            GuideFilePath = item.Path;
                        }
                    }
                    else
                    {
                        SetError(Services.L10n.Get("PDZ_ErrorTitle.Text"), Services.L10n.Get("PDZ_ErrorPdfOnly"));
                    }
                }
            }
            catch (Exception ex)
            {
                SetError(Services.L10n.Get("PDZ_ErrorReadTitle"), Services.L10n.Get("PDZ_ErrorReadMessage"));
                Serilog.Log.Warning(ex, "Erreur lors du dépôt de fichier PDF.");
            }
        }
    }

    private void OnDropBorderTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GuideFilePath))
        {
            BrowsePdfAsync();
        }
    }

    private void OnDropBorderKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.Space)
        {
            if (string.IsNullOrWhiteSpace(GuideFilePath))
            {
                e.Handled = true;
                BrowsePdfAsync();
            }
        }
    }

    private async void BrowsePdfAsync()
    {
        try
        {
            var pickerService = App.Services.GetService<PickerService>();
            if (pickerService is not null)
            {
                var file = await pickerService.PickSingleFileAsync(".pdf");
                if (file is not null && file.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(file.Path);
                    if (info.Exists && info.Length > 50 * 1024 * 1024)
                    {
                        SetError("Fichier volumineux", "La taille du guide PDF dépasse la limite de 50 Mo.");
                    }
                    else
                    {
                        GuideFilePath = file.Path;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SetError(Services.L10n.Get("PDZ_ErrorReadTitle", "Erreur de sélection"), Services.L10n.Get("PDZ_ErrorReadMessage", "Impossible d'accéder au fichier sélectionné."));
            Serilog.Log.Warning(ex, "Erreur lors de la sélection du fichier PDF.");
        }
    }

    private void OnClearFileClicked(object sender, RoutedEventArgs e)
    {
        GuideFilePath = string.Empty;
        UpdateState();
    }

    // ───────────────────────── Chapitres du guide (cache ToC) ─────────────────────────

    /// <summary>Charge la table des matières du guide (depuis le cache lorsqu'elle existe)
    /// et alimente le sélecteur de chapitres. Silencieux en cas d'échec.</summary>
    private async void LoadChapterListAsync()
    {
        try { _tocLoadCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { _tocLoadCts?.Dispose(); } catch (ObjectDisposedException) { }
        _tocLoadCts = new CancellationTokenSource();
        var ct = _tocLoadCts.Token;

        var path = GuideFilePath;
        ChapterPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var guideService = App.Services.GetService<IPdfGuideService>();
            if (guideService is null)
            {
                return;
            }

            var toc = await Task.Run(async () =>
            {
                try
                {
                    return await guideService.GetTocAsync(path, ct);
                }
                catch (OperationCanceledException) { return null; }
                catch (Exception ex)
                {
                    Serilog.Log.Debug(ex, "Table des matières du guide indisponible (chemin {Path}).", path);
                    return null;
                }
            });

            if (ct.IsCancellationRequested || toc is null || toc.Entries.Count == 0)
            {
                return;
            }

            _suppressChapterSelection = true;
            var items = new System.Collections.Generic.List<ChapterOption>(toc.Entries.Count);
            foreach (var entry in toc.Entries)
            {
                items.Add(new ChapterOption(entry));
            }
            ChapterCombo.ItemsSource = items;
            ChapterCombo.SelectedIndex = -1;
            _suppressChapterSelection = false;

            // La zone peut avoir été vidée pendant l'analyse asynchrone.
            if (string.Equals(GuideFilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                ChapterPanel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Affichage des chapitres du guide impossible.");
        }
    }

    private void OnChapterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChapterSelection) return;
        if (ChapterCombo.SelectedItem is not ChapterOption option) return;

        ChapterSelected?.Invoke(this, option.Entry);
    }

    /// <summary>Élément affichable du sélecteur : « p. 42 · Titre du chapitre ».</summary>
    public sealed class ChapterOption
    {
        public ToCEntry Entry { get; }

        public ChapterOption(ToCEntry entry) => Entry = entry;

        public override string ToString() =>
            $"p. {Entry.PrintedPage} · {Entry.Title}";
    }
}
