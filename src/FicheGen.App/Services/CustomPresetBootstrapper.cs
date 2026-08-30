using System.Text.Json;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Core.Storage;
using FicheGen.Core.Services;
using Serilog;

namespace FicheGen.App.Services;

/// <summary>
/// Réenregistre au démarrage les styles personnalisés (persistés dans <c>styles.custom.json</c>
/// et <c>style.builder.json</c>) afin que la génération, l'aperçu et les exports
/// résolvent les identifiants personnalisés via le <see cref="StylePresetService"/>.
/// </summary>
public static class CustomPresetBootstrapper
{
    private sealed record BuilderDto(string Primary, string Secondary, string Accent, string Font,
        double Radius, double MarginTop, double MarginBottom, double MarginLeft, double MarginRight,
        string? Header = null);

    private sealed record CustomPresetDto(
        string Id,
        string Name,
        string Description,
        string Icon,
        string PrimaryHex,
        string SecondaryHex,
        string AccentHex,
        string FontFamily,
        double CornerRadius,
        string HeaderLayout,
        bool IsVisible = true,
        bool IsCustom = true,
        double MarginTop = 20,
        double MarginBottom = 20,
        double MarginLeft = 20,
        double MarginRight = 20);

    public static void Register(StylePresetService stylePresetService, ISettingsStore settingsStore)
    {
        try
        {
            var settings = settingsStore.GetSettings<AppSettings>();

            // 1. Enregistre tous les thèmes personnalisés créés par l'enseignant
            if (settings.Ai.Models.TryGetValue("styles.custom.json", out var customJson) &&
                !string.IsNullOrWhiteSpace(customJson))
            {
                var customList = JsonSerializer.Deserialize<List<CustomPresetDto>>(customJson);
                if (customList != null)
                {
                    foreach (var c in customList)
                    {
                        stylePresetService.AddCustomPreset(new StylePreset
                        {
                            Id = c.Id,
                            Name = c.Name,
                            PrimaryColor = c.PrimaryHex,
                            SecondaryColor = c.SecondaryHex,
                            AccentColor = c.AccentHex,
                            FontFamily = string.IsNullOrWhiteSpace(c.FontFamily) ? "Segoe UI" : c.FontFamily,
                            MarginMm = (int)Math.Round(Math.Clamp(c.MarginTop, 5, 40)),
                            MarginBottomMm = (int)Math.Round(Math.Clamp(c.MarginBottom, 5, 40)),
                            MarginLeftMm = (int)Math.Round(Math.Clamp(c.MarginLeft, 5, 40)),
                            MarginRightMm = (int)Math.Round(Math.Clamp(c.MarginRight, 5, 40)),
                            CornerRadiusPx = Math.Clamp(c.CornerRadius, 0, 28),
                            HeaderLayout = NormalizeHeader(c.HeaderLayout)
                        });
                    }
                }
            }

            // 2. Enregistre le préréglage générique 'custom' issu du StyleBuilder
            if (settings.Ai.Models.TryGetValue("style.builder.json", out var json) &&
                !string.IsNullOrWhiteSpace(json))
            {
                var b = JsonSerializer.Deserialize<BuilderDto>(json);
                if (b != null)
                {
                    stylePresetService.AddCustomPreset(new StylePreset
                    {
                        Id = "custom",
                        Name = "Personnalisé",
                        PrimaryColor = b.Primary,
                        SecondaryColor = b.Secondary,
                        AccentColor = b.Accent,
                        FontFamily = string.IsNullOrWhiteSpace(b.Font) ? "Segoe UI" : b.Font,
                        MarginMm = (int)Math.Round(Math.Clamp(b.MarginTop, 5, 40)),
                        MarginBottomMm = (int)Math.Round(Math.Clamp(b.MarginBottom, 5, 40)),
                        MarginLeftMm = (int)Math.Round(Math.Clamp(b.MarginLeft, 5, 40)),
                        MarginRightMm = (int)Math.Round(Math.Clamp(b.MarginRight, 5, 40)),
                        CornerRadiusPx = Math.Clamp(b.Radius, 0, 28),
                        HeaderLayout = NormalizeHeader(b.Header)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Réenregistrement des styles personnalisés impossible.");
        }
    }

    private static string NormalizeHeader(string? header) => header switch
    {
        StylePreset.HeaderBand => StylePreset.HeaderBand,
        StylePreset.HeaderCentered => StylePreset.HeaderCentered,
        StylePreset.HeaderMinimal => StylePreset.HeaderMinimal,
        _ => StylePreset.HeaderRule
    };
}
