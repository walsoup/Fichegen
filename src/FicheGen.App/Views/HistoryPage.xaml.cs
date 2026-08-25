using System;
using System.Threading.Tasks;
using FicheGen.App.ViewModels;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FicheGen.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }
    private DispatcherQueueTimer? _undoTimer;
    private HistoryItemViewModel? _lastDeletedItem;
    private bool _isDialogActive;

    public static Visibility BoolToVisibility(bool val) => val ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility InverseBoolToVisibility(bool val) => val ? Visibility.Collapsed : Visibility.Visible;

    public HistoryPage()
    {
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        DataContext = ViewModel;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void FocusSearchBox()
    {
        SearchBox.Focus(FocusState.Programmatic);
    }

    public void SetSearchQuery(string query)
    {
        ViewModel.SearchQuery = query;
        SearchBox.Text = query;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        FicheGen.App.Services.UiMotion.StaggeredFadeUp(
            new FrameworkElement[] { SearchBox, FilterRadioButtons, ResultsScrollViewer });
        await ViewModel.LoadHistoryAsync();
    }

    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await ViewModel.LoadHistoryAsync();
    }

    private async void OnFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilterRadioButtons.SelectedItem is RadioButton item && item.Content is string filter)
        {
            ViewModel.SelectedTypeFilter = filter;
            await ViewModel.LoadHistoryAsync();
        }
    }

    private void OnItemClicked(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as HistoryItemViewModel
                ?? (sender as FrameworkElement)?.Tag as HistoryItemViewModel;
        if (item != null)
        {
            // Prépare le vol de la carte vers l'aperçu (animation connectée Fluent).
            try
            {
                if (sender is FrameworkElement card && FicheGen.App.Services.UiMotion.Enabled)
                {
                    Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService
                        .GetForCurrentView().PrepareToAnimate("openDoc", card);
                }
            }
            catch { /* Animation optionnelle : ne jamais bloquer l'ouverture. */ }

            ViewModel.OpenItem(item);
        }
    }

    private void OnItemKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.Space)
        {
            var item = (sender as FrameworkElement)?.DataContext as HistoryItemViewModel
                    ?? (sender as FrameworkElement)?.Tag as HistoryItemViewModel;
            if (item != null)
            {
                ViewModel.OpenItem(item);
                e.Handled = true;
            }
        }
    }

    // ───────────────────────── Survol carte par carte ─────────────────────────
    // Le survol est appliqué à la carte seule (jamais au groupe du jour entier).

    private void OnItemPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            card.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
            card.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        }
    }

    private void OnItemPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            card.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            card.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        }
    }

    private async void OnFavoriteClicked(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as HistoryItemViewModel
                ?? (sender as FrameworkElement)?.Tag as HistoryItemViewModel;
        if (item != null)
        {
            await ViewModel.ToggleFavoriteAsync(item);
        }
    }

    private void OnOpenMenuItemClicked(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as HistoryItemViewModel
                ?? (sender as FrameworkElement)?.Tag as HistoryItemViewModel;
        if (item != null)
        {
            ViewModel.OpenItem(item);
        }
    }

    private void OnExportWordMenuItemClicked(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as HistoryItemViewModel
                ?? (sender as FrameworkElement)?.Tag as HistoryItemViewModel;
        if (item != null)
        {
            ViewModel.ExportWordCommand.Execute(item);
        }
    }

    private void OnExportPdfMenuItemClicked(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as HistoryItemViewModel
                ?? (sender as FrameworkElement)?.Tag as HistoryItemViewModel;
        if (item != null)
        {
            ViewModel.ExportPdfCommand.Execute(item);
        }
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (_isDialogActive) return;

        var item = (sender as FrameworkElement)?.DataContext as HistoryItemViewModel
                ?? (sender as FrameworkElement)?.Tag as HistoryItemViewModel;
        if (item != null && this.XamlRoot != null)
        {
            _isDialogActive = true;
            try
            {
                var dialog = new ContentDialog
                {
                    Title = FicheGen.App.Services.L10n.Get("HP_DeleteTitle"),
                    Content = string.Format(FicheGen.App.Services.L10n.Get("HP_DeleteContent"), item.Title),
                    PrimaryButtonText = FicheGen.App.Services.L10n.Get("HP_DeleteConfirm"),
                    CloseButtonText = FicheGen.App.Services.L10n.Get("Dialog_Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _lastDeletedItem = item;
                    await ViewModel.DeleteItemAsync(item);
                    ShowUndoToast(item.Title);
                }
            }
            finally
            {
                _isDialogActive = false;
            }
        }
    }

    private void ShowUndoToast(string title)
    {
        UndoToastText.Text = string.Format(FicheGen.App.Services.L10n.Get("HP_UndoToast"), title);
        UndoToast.Visibility = Visibility.Visible;

        _undoTimer?.Stop();
        _undoTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _undoTimer.Interval = TimeSpan.FromSeconds(8);
        _undoTimer.Tick += (s, e) =>
        {
            _undoTimer?.Stop();
            UndoToast.Visibility = Visibility.Collapsed;
            _lastDeletedItem = null;
        };
        _undoTimer.Start();
    }

    private async void OnUndoDeleteClicked(object sender, RoutedEventArgs e)
    {
        _undoTimer?.Stop();
        UndoToast.Visibility = Visibility.Collapsed;

        if (_lastDeletedItem?.Model is not null)
        {
            var repo = App.Services.GetService<IHistoryRepository>();
            if (repo is not null)
            {
                await repo.SaveAsync(_lastDeletedItem.Model);
                await ViewModel.LoadHistoryAsync();
            }
        }
        _lastDeletedItem = null;
    }

    private void OnDismissUndoClicked(object sender, RoutedEventArgs e)
    {
        _undoTimer?.Stop();
        UndoToast.Visibility = Visibility.Collapsed;
        _lastDeletedItem = null;
    }
}
