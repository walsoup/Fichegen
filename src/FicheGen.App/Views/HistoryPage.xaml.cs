using FicheGen.App.ViewModels;
using FicheGen.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FicheGen.App.Views;

public partial class HistoryPage : Page
{
    public HistoryViewModel? ViewModel => DataContext as HistoryViewModel;

    public HistoryPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void FocusSearchBox()
    {
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.LoadHistoryAsync();
        }
    }

    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (ViewModel != null)
        {
            await ViewModel.LoadHistoryAsync();
        }
    }

    private async void OnFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null && FilterRadioButtons.SelectedItem is RadioButton item && item.Content is string filter)
        {
            ViewModel.SelectedTypeFilter = filter;
            await ViewModel.LoadHistoryAsync();
        }
    }

    private void OnItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HistoryItemViewModel item } && ViewModel != null)
        {
            ViewModel.OpenItem(item);
        }
    }

    private async void OnFavoriteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryItemViewModel item } && ViewModel != null)
        {
            await ViewModel.ToggleFavoriteAsync(item);
        }
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryItemViewModel item } && ViewModel != null)
        {
            await ViewModel.DeleteItemAsync(item);
        }
    }
}
