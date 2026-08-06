using FicheGen.Core.Documents;

namespace FicheGen.Core.Services;

public class StylePresetService
{
    private static readonly Dictionary<string, StylePreset> BuiltInPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["modern"] = new StylePreset
        {
            Id = "modern",
            Name = "Moderne",
            PrimaryColor = "#2563EB",
            SecondaryColor = "#1E40AF",
            FontFamily = "'Segoe UI', system-ui, -apple-system, sans-serif",
            MarginMm = 20
        },
        ["classic"] = new StylePreset
        {
            Id = "classic",
            Name = "Classique",
            PrimaryColor = "#1E293B",
            SecondaryColor = "#475569",
            FontFamily = "Georgia, 'Times New Roman', serif",
            MarginMm = 25
        },
        ["minimal"] = new StylePreset
        {
            Id = "minimal",
            Name = "Minimaliste",
            PrimaryColor = "#000000",
            SecondaryColor = "#525252",
            FontFamily = "'Segoe UI', system-ui, sans-serif",
            MarginMm = 15
        },
        ["academic"] = new StylePreset
        {
            Id = "academic",
            Name = "Académique",
            PrimaryColor = "#7C2D12",
            SecondaryColor = "#9A3412",
            FontFamily = "'Times New Roman', Times, serif",
            MarginMm = 25
        },
        ["playful"] = new StylePreset
        {
            Id = "playful",
            Name = "Ludique",
            PrimaryColor = "#0D9488",
            SecondaryColor = "#0F766E",
            FontFamily = "'Avenir', 'Segoe UI', sans-serif",
            MarginMm = 18
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
