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

        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = primaryButtonText,
                XamlRoot = _xamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Impossible d'afficher le dialogue d'information : {Title}", title);
        }
    }

    public async Task<bool> ShowConfirmationAsync(string title, string content, string confirmButtonText = "Confirmer", string cancelButtonText = "Annuler")
    {
        if (_xamlRoot == null) return false;

        try
        {
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Impossible d'afficher le dialogue de confirmation : {Title}", title);
            return false;
        }
    }
}
