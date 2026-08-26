using FicheGen.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace FicheGen.App.Views.Controls;

public sealed partial class AccountDialog : ContentDialog
{
    public AccountViewModel ViewModel { get; }

    public AccountDialog()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<AccountViewModel>();
    }
}
