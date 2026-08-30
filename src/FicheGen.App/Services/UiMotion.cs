using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace FicheGen.App.Services;

/// <summary>
/// Petites animations d'interface (entrées en douceur, pulsations).
/// Respecte le réglage Windows « Effets d'animation » : si l'utilisateur
/// les désactive, aucun mouvement n'est joué et les éléments restent visibles.
/// </summary>
public static class UiMotion
{
    public static bool Enabled => ReadAnimationsEnabled();

    private static bool ReadAnimationsEnabled()
    {
        try { return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return true; }
    }

    /// <summary>
    /// Fait entrer une série d'éléments en fondu + glissement vertical,
    /// décalés les uns après les autres (rythme « cascade »).
    /// </summary>
    public static void StaggeredFadeUp(IEnumerable<FrameworkElement?>? elements,
                                       double offsetY = 14,
                                       TimeSpan? step = null,
                                       TimeSpan? duration = null)
    {
        if (!Enabled || elements == null) return;

        var stepTs = step ?? TimeSpan.FromMilliseconds(50);
        var dur = duration ?? TimeSpan.FromMilliseconds(340);
        var delay = TimeSpan.Zero;

        foreach (var el in elements)
        {
            if (el is null) continue;

            var translate = new TranslateTransform();
            el.RenderTransform = translate;
            el.Opacity = 0;

            var sb = new Storyboard();

            var fade = new DoubleAnimation
            {
                From = 0, To = 1,
                Duration = new Duration(dur),
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, el);
            Storyboard.SetTargetProperty(fade, "Opacity");

            var rise = new DoubleAnimation
            {
                From = offsetY, To = 0,
                Duration = new Duration(dur),
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(rise, translate);
            Storyboard.SetTargetProperty(rise, "Y");

            sb.Children.Add(fade);
            sb.Children.Add(rise);
            sb.Begin();

            delay += stepTs;
        }
    }

    /// <summary>Variante mono-élément de <see cref="StaggeredFadeUp"/>.</summary>
    public static void FadeUp(FrameworkElement? element, double offsetY = 10)
        => StaggeredFadeUp(new[] { element }, offsetY);
}
