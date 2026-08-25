using FicheGen.Core.Documents;

namespace FicheGen.Core.Services;

public class StylePresetService
{
    private static readonly Dictionary<string, StylePreset> BuiltInPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        // Cinq directions artistiques d'inspiration éditoriale française :
        // Le traitement d'en-tête, la typographie, les contrastes et les encadrés
        // confèrent à chaque document une identité visuelle soignée et distincte.
        ["modern"] = new StylePreset
        {
            Id = "modern",
            Name = "Moderne",
            PrimaryColor = "#1E3A8A",
            SecondaryColor = "#2563EB",
            AccentColor = "#D97706",
            FontFamily = "'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif",
            MarginMm = 20,
            CornerRadiusPx = 8,
            HeaderLayout = StylePreset.HeaderRule
        },
        ["classic"] = new StylePreset
        {
            Id = "classic",
            Name = "Classique",
            PrimaryColor = "#1E3A5F",
            SecondaryColor = "#475569",
            AccentColor = "#991B1B",
            FontFamily = "Georgia, 'Baskerville', 'Times New Roman', serif",
            MarginMm = 24,
            CornerRadiusPx = 3,
            HeaderLayout = StylePreset.HeaderCentered
        },
        ["minimal"] = new StylePreset
        {
            Id = "minimal",
            Name = "Minimaliste",
            PrimaryColor = "#0F172A",
            SecondaryColor = "#475569",
            AccentColor = "#059669",
            FontFamily = "'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif",
            MarginMm = 18,
            CornerRadiusPx = 2,
            HeaderLayout = StylePreset.HeaderMinimal
        },
        ["academic"] = new StylePreset
        {
            Id = "academic",
            Name = "Académique",
            PrimaryColor = "#311042",
            SecondaryColor = "#4338CA",
            AccentColor = "#B45309",
            FontFamily = "Cambria, 'Palatino Linotype', 'Georgia', serif",
            MarginMm = 25,
            CornerRadiusPx = 4,
            HeaderLayout = StylePreset.HeaderCentered
        },
        ["playful"] = new StylePreset
        {
            Id = "playful",
            Name = "Ludique",
            PrimaryColor = "#4F46E5",
            SecondaryColor = "#DB2777",
            AccentColor = "#F59E0B",
            FontFamily = "'Trebuchet MS', 'Segoe UI Variable Display', 'Segoe UI', sans-serif",
            MarginMm = 18,
            CornerRadiusPx = 14,
            HeaderLayout = StylePreset.HeaderBand
        }
    };

    private readonly Dictionary<string, StylePreset> _customPresets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<StylePreset> GetPresets()
    {
        var result = new List<StylePreset>(BuiltInPresets.Values);
        result.AddRange(_customPresets.Values);
        return result.AsReadOnly();
    }

    public StylePreset GetPreset(string? presetId)
    {
        if (!string.IsNullOrEmpty(presetId))
        {
            if (BuiltInPresets.TryGetValue(presetId, out var builtIn)) return builtIn;
            if (_customPresets.TryGetValue(presetId, out var custom)) return custom;
        }

        return BuiltInPresets["modern"];
    }

    public StylePreset AddCustomPreset(StylePreset preset)
    {
        var sanitizedCss = CssSanitizer.SanitizeCss(preset.CustomCss);
        var sanitizedPreset = preset with { CustomCss = sanitizedCss };
        _customPresets[preset.Id] = sanitizedPreset;
        return sanitizedPreset;
    }

    public string GenerateCss(string? presetId)
    {
        var preset = GetPreset(presetId);
        var sanitizedCustom = CssSanitizer.SanitizeCss(preset.CustomCss);

        return $$"""
            :root {
                --primary-color: {{preset.PrimaryColor}};
                --secondary-color: {{preset.SecondaryColor}};
                --font-family: {{preset.FontFamily}};
                --page-margin: {{preset.MarginMm}}mm;
            }
            body {
                font-family: var(--font-family);
                margin: var(--page-margin);
                color: #1f2937;
            }
            h1, h2, h3 {
                color: var(--primary-color);
            }
            .callout-box {
                border-left: 4px solid var(--secondary-color);
                background-color: #f8fafc;
            }
            {{sanitizedCustom}}
            """;
    }
}
