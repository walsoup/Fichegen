using System.Text.Json;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Core.Storage;
using FicheGen.Core.Services;
using Serilog;

namespace FicheGen.App.Services;

/// <summary>
/// Réenregistre au démarrage le style « Personnalisé » issu du StyleBuilder
/// (persisté dans <c>style.builder.json</c>) afin que la génération, l'aperçu
/// et les exports résolvent l'identifiant <c>custom</c> via le <see cref="StylePresetService"/>.
/// </summary>
public static class CustomPresetBootstrapper
{
    private sealed record BuilderDto(string Primary, string Secondary, string Accent, string Font,
        double Radius, double MarginTop, double MarginBottom, double MarginLeft, double MarginRight,
        string? Header = null);

    public static void Register(StylePresetService stylePresetService, ISettingsStore settingsStore)
    {
        try
        {
            var settings = settingsStore.GetSettings<AppSettings>();
            if (!settings.Ai.Models.TryGetValue("style.builder.json", out var json) ||
                string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var b = JsonSerializer.Deserialize<BuilderDto>(json);
            if (b is null) return;

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
        catch (Exception ex)
        {
            // Style personnalisé illisible : les préréglages intégrés restent disponibles.
            Log.Debug(ex, "Réenregistrement du style personnalisé (StyleBuilder) impossible.");
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
