using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FicheGen.App.Views.Controls;

public sealed partial class GenerateButton : UserControl
{
    public static readonly DependencyProperty IsGeneratingProperty =
        DependencyProperty.Register(nameof(IsGenerating), typeof(bool), typeof(GenerateButton), new PropertyMetadata(false));

    public static readonly DependencyProperty ButtonTextProperty =
        DependencyProperty.Register(nameof(ButtonText), typeof(string), typeof(GenerateButton), new PropertyMetadata("Générer (Ctrl+G)"));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(GenerateButton), new PropertyMetadata(null));

    public static readonly DependencyProperty CancelCommandProperty =
        DependencyProperty.Register(nameof(CancelCommand), typeof(ICommand), typeof(GenerateButton), new PropertyMetadata(null));

    public bool IsGenerating
    {
        get => (bool)GetValue(IsGeneratingProperty);
        set => SetValue(IsGeneratingProperty, value);
    }

    public string ButtonText
    {
        get => (string)GetValue(ButtonTextProperty);
        set => SetValue(ButtonTextProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public GenerateButton()
    {
        InitializeComponent();
    }

    private void OnMainButtonClicked(object sender, RoutedEventArgs e)
    {
        if (Command?.CanExecute(null) == true)
        {
            Command.Execute(null);
        }
    }

    private void OnCancelButtonClicked(object sender, RoutedEventArgs e)
    {
        if (CancelCommand?.CanExecute(null) == true)
        {
            CancelCommand.Execute(null);
        }
    }
}
