using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using FicheGen.App.Services;

namespace FicheGen.App.Views.Controls;

/// <summary>
/// Palette de commandes façon « Ctrl+K » : recherche instantanée dans toutes les
/// actions de l'application, navigation clavier intégrale, fermeture par Échap
/// ou clic sur le voile. Le shell fournit les actions contextuelles via <see cref="Open"/>;
/// la palette exécute elle-même l'action choisie.
/// </summary>
public sealed partial class CommandPalette : UserControl
{
    /// <summary>Une action proposée par la palette.</summary>
    public sealed record CommandAction(string Title, string Subtitle, string Glyph, string Hint, Action Run);

    private IReadOnlyList<CommandAction> _all = Array.Empty<CommandAction>();
    private List<CommandAction> _visible = new();
    private bool _suppressQueryFilter;
    private Control? _invokingElement;

    /// <summary>Levée après exécution d'une action (utile pour rendre le focus au shell).</summary>
    public event EventHandler<CommandAction>? ActionInvoked;

    public bool IsOpen => Overlay.Visibility == Visibility.Visible;

    public CommandPalette()
    {
        InitializeComponent();
    }

    // ───────────────────────── Ouverture / fermeture ─────────────────────────

    public void Open(IReadOnlyList<CommandAction> actions)
    {
        _invokingElement = FocusManager.GetFocusedElement(XamlRoot) as Control;
        _all = actions;
        _suppressQueryFilter = true;
        QueryBox.Text = string.Empty;
        _suppressQueryFilter = false;

        Overlay.Visibility = Visibility.Visible;
        ApplyFilter(string.Empty);

        if (UiMotion.Enabled)
        {
            Panel.Opacity = 0;
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0, To = 1,
                Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromMilliseconds(160)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, Panel);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Begin();
        }

        DispatcherQueue.TryEnqueue(() => QueryBox.Focus(FocusState.Keyboard));
    }

    public void Close()
    {
        Overlay.Visibility = Visibility.Collapsed;
        _invokingElement?.Focus(FocusState.Programmatic);
        _invokingElement = null;
    }

    // ───────────────────────── Filtrage & exécution ─────────────────────────

    private void ApplyFilter(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _visible = tokens.Length == 0
            ? _all.ToList()
            : _all.Where(a => tokens.All(t =>
                  a.Title.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                  a.Subtitle.Contains(t, StringComparison.OrdinalIgnoreCase)))
                 .ToList();

        ResultsList.ItemsSource = _visible;
        ResultsList.SelectedIndex = _visible.Count > 0 ? 0 : -1;
    }

    private void Invoke(CommandAction? action)
    {
        if (action is null) return;
        Close();
        action.Run();
        ActionInvoked?.Invoke(this, action);
    }

    // ───────────────────────── Événements XAML ─────────────────────────

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressQueryFilter) return;
        ApplyFilter(QueryBox.Text);
    }

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Down when _visible.Count > 0:
                ResultsList.SelectedIndex = Math.Min(ResultsList.SelectedIndex + 1, _visible.Count - 1);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Up:
                ResultsList.SelectedIndex = Math.Max(ResultsList.SelectedIndex - 1, 0);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Enter:
                Invoke(ResultsList.SelectedItem as CommandAction ?? _visible.FirstOrDefault());
                e.Handled = true;
                break;
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
        => Invoke(e.ClickedItem as CommandAction);

    private void OnScrimTapped(object sender, TappedRoutedEventArgs e) => Close();
}
