using FicheGen.Core.Abstractions;
using FicheGen.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Color = Windows.UI.Color;
using Colors = Microsoft.UI.Colors;

namespace FicheGen.App.Services;

/// <summary>
/// Applique la couleur d'accentuation PROFstudio à toute l'interface : remplace
/// les ressources système <c>SystemAccentColor*</c> et les pinceaux d'accent
/// Fluent au niveau de l'application. Les références <c>{ThemeResource}</c> des
/// modèles de contrôles sont réévaluées, le changement est donc immédiat.
/// </summary>
public sealed class AccentColorService
{
    public const string DefaultHex = "#2563EB";

    private readonly ISettingsStore _settingsStore;

    public AccentColorService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    /// <summary>Applique l'accentuation persistée (démarrage de l'application).</summary>
    public void ApplyFromSettings()
    {
        string hex = DefaultHex;
        try
        {
            var settings = _settingsStore.GetSettings<AppSettings>();
            if (!string.IsNullOrWhiteSpace(settings.Ui.AccentColor))
            {
                hex = settings.Ui.AccentColor;
            }
            else if (settings.Ai.Models.TryGetValue("ui.accent", out var legacy)
                     && !string.IsNullOrWhiteSpace(legacy))
            {
                // Migration : ancien emplacement clé/valeur de l'accentuation.
                hex = legacy;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Lecture de la couleur d'accentuation impossible — valeur par défaut.");
        }

        Apply(hex);
    }

    /// <summary>Applique une teinte d'accentuation (format #RRGGBB) à l'interface.</summary>
    public void Apply(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            hex = DefaultHex;
        }

        Color baseColor;
        try
        {
            baseColor = ParseHex(hex);
        }
        catch
        {
            baseColor = ParseHex(DefaultHex);
        }

        var resources = Application.Current.Resources;

        // ── Couleurs système (référencées par de nombreux modèles de contrôles) ──
        resources["SystemAccentColor"] = baseColor;
        resources["SystemAccentColorLight1"] = Blend(baseColor, Colors.White, 0.28);
        resources["SystemAccentColorLight2"] = Blend(baseColor, Colors.White, 0.48);
        resources["SystemAccentColorLight3"] = Blend(baseColor, Colors.White, 0.68);
        resources["SystemAccentColorDark1"] = Blend(baseColor, Colors.Black, 0.22);
        resources["SystemAccentColorDark2"] = Blend(baseColor, Colors.Black, 0.42);
        resources["SystemAccentColorDark3"] = Blend(baseColor, Colors.Black, 0.62);

        // ── Pinceaux d'accent Fluent 2 (prioritaires sur les dictionnaires fusionnés) ──
        resources["AccentFillColorDefaultBrush"] = Solid(baseColor);
        resources["AccentFillColorSecondaryBrush"] = Solid(WithAlpha(baseColor, 0xE6));
        resources["AccentFillColorTertiaryBrush"] = Solid(WithAlpha(baseColor, 0xC7));
        resources["AccentFillColorDisabledBrush"] = Solid(WithAlpha(baseColor, 0x5D));
        resources["AccentTextFillColorPrimaryBrush"] = Solid(baseColor);
        resources["AccentTextFillColorSecondaryBrush"] = Solid(Blend(baseColor, Colors.Black, 0.18));
        resources["AccentTextFillColorTertiaryBrush"] = Solid(Blend(baseColor, Colors.Black, 0.34));
        resources["AccentTextFillColorDisabledBrush"] = Solid(WithAlpha(baseColor, 0x5D));
        resources["AccentButtonBackground"] = Solid(baseColor);
        resources["AccentButtonForeground"] = Solid(Colors.White);
        resources["AccentButtonBorderBrush"] = Solid(WithAlpha(Colors.Black, 0x0F));

        // Les références {ThemeResource} déjà résolues ne se reévaluent qu'avec un
        // changement de thème : on bascule brièvement le thème de la racine pour
        // forcer la propagation, puis on rétablit. Sans cela, la nouvelle teinte
        // n'apparaît qu'au prochain démarrage.
        try
        {
            if (Application.Current is App app && app.MainWindow?.Content is FrameworkElement root)
            {
                var current = root.RequestedTheme;
                root.RequestedTheme = current == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                root.RequestedTheme = current;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Rafraîchissement immédiat de l'accentuation impossible — appliquée au prochain démarrage.");
        }
    }

    private static SolidColorBrush Solid(Color color) => new(color);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        255,
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8)
        {
            return Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        }

        return Color.FromArgb(255,
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }
}
