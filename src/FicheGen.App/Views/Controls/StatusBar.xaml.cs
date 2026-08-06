using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FicheGen.App.Views.Controls;

public sealed partial class StatusBar : UserControl
{
    public static readonly DependencyProperty StatusMessageProperty =
        DependencyProperty.Register(nameof(StatusMessage), typeof(string), typeof(StatusBar), new PropertyMetadata("Prêt"));

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(StatusBar), new PropertyMetadata(false));

    public static readonly DependencyProperty ProviderBadgeTextProperty =
        DependencyProperty.Register(nameof(ProviderBadgeText), typeof(string), typeof(StatusBar), new PropertyMetadata("Google AI Studio"));

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string ProviderBadgeText
    {
        get => (string)GetValue(ProviderBadgeTextProperty);
        set => SetValue(ProviderBadgeTextProperty, value);
    }

    public StatusBar()
    {
        InitializeComponent();
    }
}
