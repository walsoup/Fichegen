using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FicheGen.App.Services;

public sealed class DialogService
{
    private XamlRoot? _xamlRoot;

    public void Initialize(XamlRoot xamlRoot)
    {
        _xamlRoot = xamlRoot;
    }

    public async Task ShowMessageAsync(string title, string content, string primaryButtonText = "OK")
    {
        if (_xamlRoot == null) return;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = primaryButtonText,
            XamlRoot = _xamlRoot
        };

        await dialog.ShowAsync();
    }

    public async Task<bool> ShowConfirmationAsync(string title, string content, string confirmButtonText = "Confirmer", string cancelButtonText = "Annuler")
    {
        if (_xamlRoot == null) return false;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = confirmButtonText,
            CloseButtonText = cancelButtonText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
