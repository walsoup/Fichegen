using FicheGen.Core.Diff;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FicheGen.App.Views.Controls;

public sealed class DiffDisplayLine
{
    public string Sign { get; init; } = " ";
    public string Text { get; init; } = string.Empty;
    public Brush SignColor { get; init; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
    public Brush TextColor { get; init; } = new SolidColorBrush(Microsoft.UI.Colors.Black);
}

public partial class DiffCard : UserControl
{
    public static readonly DependencyProperty DiffLinesProperty =
        DependencyProperty.Register(
            nameof(DiffLines),
            typeof(IReadOnlyList<DiffLine>),
            typeof(DiffCard),
            new PropertyMetadata(null, OnDiffLinesChanged));

    public IReadOnlyList<DiffLine>? DiffLines
    {
        get => (IReadOnlyList<DiffLine>?)GetValue(DiffLinesProperty);
        set => SetValue(DiffLinesProperty, value);
    }

    public DiffCard()
    {
        InitializeComponent();
    }

    private static void OnDiffLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiffCard card && e.NewValue is IReadOnlyList<DiffLine> lines)
        {
            card.RenderDiff(lines);
        }
    }

    private void RenderDiff(IReadOnlyList<DiffLine> lines)
    {
        var displayLines = new List<DiffDisplayLine>();
        var greenBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94)); // Green
        var redBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));   // Red
        var grayBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)); // Slate

        foreach (var line in lines)
        {
            displayLines.Add(line.Kind switch
            {
                DiffKind.Added => new DiffDisplayLine { Sign = "+", Text = line.Text, SignColor = greenBrush, TextColor = greenBrush },
                DiffKind.Removed => new DiffDisplayLine { Sign = "-", Text = line.Text, SignColor = redBrush, TextColor = redBrush },
                DiffKind.Collapsed => new DiffDisplayLine { Sign = "@@", Text = line.Text, SignColor = grayBrush, TextColor = grayBrush },
                _ => new DiffDisplayLine { Sign = " ", Text = line.Text, SignColor = grayBrush, TextColor = grayBrush }
            });
        }

        DiffItemsControl.ItemsSource = displayLines;
    }
}
