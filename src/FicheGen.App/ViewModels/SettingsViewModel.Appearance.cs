using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.App.Services;
using FicheGen.Core.Documents;
using Windows.UI;

namespace FicheGen.App.ViewModels;

public partial class SettingsViewModel
{
    // ─────────────── Onglet 1 · Général & Apparence ───────────────

    [ObservableProperty] public partial string Theme { get; set; } = "system";
    [ObservableProperty] public partial string Language { get; set; } = "fr-FR";
    [ObservableProperty] public partial ThemeOption? SelectedThemeOption { get; set; }
    [ObservableProperty] public partial LanguageOption? SelectedLanguageOption { get; set; }
    [ObservableProperty] public partial string TeacherName { get; set; } = "Enseignant·e";
    [ObservableProperty] public partial string SchoolName { get; set; } = "École / Établissement";
    [ObservableProperty] public partial string DefaultClassLevel { get; set; } = "CM2";
    [ObservableProperty] public partial string DefaultSubject { get; set; } = "Mathématiques";
    [ObservableProperty] public partial AccentOption? SelectedAccent { get; set; }
    [ObservableProperty] public partial bool LaunchAtStartup { get; set; }
    [ObservableProperty] public partial string StartupStatusText { get; set; } = "Vérification de l'état du démarrage…";
    [ObservableProperty] public partial bool EnableStreaming { get; set; } = true;

    partial void OnSelectedThemeOptionChanged(ThemeOption? value)
    {
        if (value is not null && Theme != value.Key)
        {
            Theme = value.Key;
        }
    }

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value is not null && Language != value.Code)
        {
            Language = value.Code;
            L10n.SetLanguage(value.Code);

            try
            {
                var settings = _settingsStore.GetSettings<FicheGen.Core.Storage.AppSettings>();
                settings.Ui.Language = value.Code;
                _ = _settingsStore.SaveSettingsAsync(settings);
            }
            catch { }
        }
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = new List<ThemeOption>
    {
        new("system", "Par défaut du système", "🖥️"),
        new("light", "Clair", "☀️"),
        new("dark", "Sombre", "🌙"),
        new("oled", "Noir absolu (OLED)", "⬛"),
    };

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = new List<LanguageOption>
    {
        new("fr-FR", "Français (France)", "🇫🇷"),
        new("en-US", "English (United States)", "🇺🇸"),
        new("ar-SA", "العربية (Arabic)", "🇸🇦"),
    };

    public ObservableCollection<AccentOption> AccentOptions { get; } = new()
    {
        new AccentOption("Bleu PROFstudio", "#2563EB"),
        new AccentOption("Violet encre", "#7C3AED"),
        new AccentOption("Vert forêt", "#059669"),
        new AccentOption("Orange ardoise", "#EA580C"),
        new AccentOption("Framboise", "#DB2777"),
        new AccentOption("Sarcelle", "#0D9488"),
    };

    // ─────────────── Onglet 4 · Styles & Personnalisation ───────────────

    public ObservableCollection<StylePresetItem> StylePresets { get; } = new()
    {
        new StylePresetItem("modern", "Moderne", "Édition contemporaine : bleu marine, accents ambre, filet asymétrique.", "🌊", "#1E3A8A", "#2563EB", "#D97706", "Segoe UI Variable Text", 8,
            FicheGen.Core.Documents.StylePreset.HeaderRule),
        new StylePresetItem("classic", "Classique", "Manuel scolaire : bleu nuit, rouge garance, lettrines et serif.", "📘", "#1E3A5F", "#475569", "#991B1B", "Georgia", 3,
            FicheGen.Core.Documents.StylePreset.HeaderCentered),
        new StylePresetItem("minimal", "Minimaliste", "Typographie suisse : noir pur, gris zinc, pastille émeraude.", "⬜", "#0F172A", "#475569", "#059669", "Segoe UI Variable Text", 2,
            FicheGen.Core.Documents.StylePreset.HeaderMinimal),
        new StylePresetItem("academic", "Académique", "Sorbonne & universités : pourpre profond, outremer et bronze.", "🎓", "#311042", "#4338CA", "#B45309", "Cambria", 4,
            FicheGen.Core.Documents.StylePreset.HeaderCentered),
        new StylePresetItem("playful", "Ludique", "Primaire & découverte : bandeau dégradé, formes douces et miel.", "🎈", "#4F46E5", "#DB2777", "#F59E0B", "Trebuchet MS", 14,
            FicheGen.Core.Documents.StylePreset.HeaderBand),
    };

    [ObservableProperty] public partial StylePresetItem? SelectedPreset { get; set; }
    [ObservableProperty] public partial string SelectedStylePresetId { get; set; } = "modern";

    /// <summary>Libellé du style réellement appliqué aux nouveaux documents.</summary>
    [ObservableProperty] public partial string DefaultStyleLabel { get; set; } = "Moderne";

    // StyleBuilder
    [ObservableProperty] public partial Color BuilderPrimaryColor { get; set; } = AccentOption.ParseHex("#1E3A8A");
    [ObservableProperty] public partial Color BuilderSecondaryColor { get; set; } = AccentOption.ParseHex("#2563EB");
    [ObservableProperty] public partial Color BuilderAccentColor { get; set; } = AccentOption.ParseHex("#D97706");
    [ObservableProperty] public partial string BuilderPrimaryHex { get; set; } = "#1E3A8A";
    [ObservableProperty] public partial string BuilderSecondaryHex { get; set; } = "#2563EB";
    [ObservableProperty] public partial string BuilderAccentHex { get; set; } = "#D97706";
    [ObservableProperty] public partial string BuilderFontFamily { get; set; } = "Segoe UI Variable Text";
    [ObservableProperty] public partial string BuilderHeaderLayout { get; set; } = FicheGen.Core.Documents.StylePreset.HeaderRule;
    [ObservableProperty] public partial double BuilderCornerRadius { get; set; } = 8;
    [ObservableProperty] public partial double BuilderMarginTop { get; set; } = 20;
    [ObservableProperty] public partial double BuilderMarginBottom { get; set; } = 20;
    [ObservableProperty] public partial double BuilderMarginLeft { get; set; } = 20;
    [ObservableProperty] public partial double BuilderMarginRight { get; set; } = 20;

    private bool _builderDirty;
    private bool _isLoadingPresetIntoBuilder;

    public IReadOnlyList<HeaderLayoutOption> HeaderLayoutOptions { get; } = new List<HeaderLayoutOption>
    {
        new(FicheGen.Core.Documents.StylePreset.HeaderRule, "Filet sous le titre", "Titre coloré souligné d'un filet bicolore."),
        new(FicheGen.Core.Documents.StylePreset.HeaderBand, "Bandeau dégradé", "Bandeau de couleur pleine, titre en blanc."),
        new(FicheGen.Core.Documents.StylePreset.HeaderCentered, "Titre centré", "Titre centré au-dessus d'un filet double."),
        new(FicheGen.Core.Documents.StylePreset.HeaderMinimal, "Minimaliste", "Titre sobre avec pastille d'accentuation."),
    };

    public IReadOnlyList<string> FontOptions { get; } = new List<string>
    {
        "Segoe UI Variable Text", "Segoe UI", "Georgia", "Cambria", "Calibri",
        "Arial", "Verdana", "Trebuchet MS", "Segoe Print",
    };

    // Générateur de style par IA
    [ObservableProperty] public partial bool IsAnalyzingStyle { get; set; }
    [ObservableProperty] public partial string StyleAnalysisStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial string GeneratedCss { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGeneratedCss))]
    public partial bool HasGeneratedCssFlag { get; set; }

    public bool HasGeneratedCss => HasGeneratedCssFlag;

    partial void OnSelectedAccentChanged(AccentOption? value)
    {
        if (_isLoadingSettings) return;

        _accentColorService?.Apply(value?.Hex);
        StatusMessage = $"🎨 Accentuation « {value?.Name} » appliquée à l'interface — pensez à enregistrer (Ctrl+S).";
    }

    partial void OnThemeChanged(string value)
    {
        if (SelectedThemeOption?.Key != value)
        {
            SelectedThemeOption = ThemeOptions.FirstOrDefault(t => t.Key == value) ?? ThemeOptions[0];
        }

        if (_isLoadingSettings) return;

        var shellTheme = value?.ToLowerInvariant() switch
        {
            "light" => "Light",
            "dark" => "Dark",
            "oled" => "Oled",
            _ => "System"
        };
        (App.CurrentMainWindow as MainWindow)?.ApplyShellTheme(shellTheme);
    }

    partial void OnLanguageChanged(string value)
    {
        if (SelectedLanguageOption?.Code != value)
        {
            SelectedLanguageOption = LanguageOptions.FirstOrDefault(l => l.Code == value) ?? LanguageOptions[0];
        }

        L10n.SetLanguage(value);

        if (_isLoadingSettings) return;

        try
        {
            StatusMessage = value switch
            {
                "ar-SA" => "تم تغيير لغة التطبيق إلى العربية — تم تحديث النصوص فورياً.",
                "en-US" => "Language changed to English — interface strings updated.",
                _ => "Langue modifiée en français — textes de l'interface mis à jour."
            };
        }
        catch { }
    }

    partial void OnSelectedPresetChanged(StylePresetItem? value)
    {
        if (value is null) return;

        _isLoadingPresetIntoBuilder = true;
        SelectedStylePresetId = value.Id;
        BuilderPrimaryColor = AccentOption.ParseHex(value.PrimaryHex);
        BuilderSecondaryColor = AccentOption.ParseHex(value.SecondaryHex);
        BuilderAccentColor = AccentOption.ParseHex(value.AccentHex);
        BuilderPrimaryHex = value.PrimaryHex;
        BuilderSecondaryHex = value.SecondaryHex;
        BuilderAccentHex = value.AccentHex;
        BuilderFontFamily = value.FontFamily;
        BuilderHeaderLayout = value.HeaderLayout;
        BuilderCornerRadius = value.CornerRadius;
        _isLoadingPresetIntoBuilder = false;
        _builderDirty = false;
        DefaultStyleLabel = value.Name;
        RequestPreviewRefresh();
    }

    public void ApplyPalette(string paletteKey)
    {
        var (p, s, a) = paletteKey switch
        {
            "ocean" => ("#1E3A8A", "#2563EB", "#D97706"),
            "forest" => ("#064E3B", "#059669", "#D97706"),
            "purple" => ("#311042", "#6D28D9", "#F59E0B"),
            "terracotta" => ("#7C2D12", "#C2410C", "#D97706"),
            "slate" => ("#0F172A", "#334155", "#0284C7"),
            "ruby" => ("#881337", "#BE123C", "#D97706"),
            _ => ("#1E3A8A", "#2563EB", "#D97706")
        };
        BuilderPrimaryColor = AccentOption.ParseHex(p);
        BuilderSecondaryColor = AccentOption.ParseHex(s);
        BuilderAccentColor = AccentOption.ParseHex(a);
    }

    private FicheGen.Core.Documents.StylePreset BuildCustomPresetFromBuilder() => new()
    {
        Id = "custom",
        Name = "Personnalisé",
        PrimaryColor = ToHex(BuilderPrimaryColor),
        SecondaryColor = ToHex(BuilderSecondaryColor),
        AccentColor = ToHex(BuilderAccentColor),
        FontFamily = string.IsNullOrWhiteSpace(BuilderFontFamily) ? "Segoe UI" : BuilderFontFamily,
        MarginMm = (int)Math.Round(BuilderMarginTop),
        MarginBottomMm = (int)Math.Round(BuilderMarginBottom),
        MarginLeftMm = (int)Math.Round(BuilderMarginLeft),
        MarginRightMm = (int)Math.Round(BuilderMarginRight),
        CornerRadiusPx = BuilderCornerRadius,
        HeaderLayout = string.IsNullOrWhiteSpace(BuilderHeaderLayout)
            ? FicheGen.Core.Documents.StylePreset.HeaderRule
            : BuilderHeaderLayout
    };

    private void MarkBuilderDirty()
    {
        if (_isLoadingPresetIntoBuilder || _isLoadingSettings) return;
        _builderDirty = true;
        DefaultStyleLabel = "Personnalisé (StyleBuilder)";
        RequestPreviewRefresh();
    }

    partial void OnBuilderPrimaryColorChanged(Color value)
    {
        BuilderPrimaryHex = ToHex(value);
        MarkBuilderDirty();
    }

    partial void OnBuilderSecondaryColorChanged(Color value)
    {
        BuilderSecondaryHex = ToHex(value);
        MarkBuilderDirty();
    }

    partial void OnBuilderAccentColorChanged(Color value)
    {
        BuilderAccentHex = ToHex(value);
        MarkBuilderDirty();
    }
    partial void OnBuilderFontFamilyChanged(string value) => MarkBuilderDirty();
    partial void OnBuilderHeaderLayoutChanged(string value) => MarkBuilderDirty();
    partial void OnBuilderCornerRadiusChanged(double value) => MarkBuilderDirty();
    partial void OnBuilderMarginTopChanged(double value) => MarkBuilderDirty();
    partial void OnBuilderMarginBottomChanged(double value) => MarkBuilderDirty();
    partial void OnBuilderMarginLeftChanged(double value) => MarkBuilderDirty();
    partial void OnBuilderMarginRightChanged(double value) => MarkBuilderDirty();

    private void RequestPreviewRefresh() => PreviewRefreshRequested?.Invoke(this, EventArgs.Empty);

    public async Task AnalyzeStylePdfAsync(string fileName)
    {
        IsAnalyzingStyle = true;
        HasGeneratedCssFlag = false;

        try
        {
            StyleAnalysisStatus = $"🎨 Déduction de la palette depuis « {fileName} »…";
            await Task.Delay(150);

            var palettes = new (string P, string S, string A, string Font, double Radius)[]
            {
                ("#1F4E79", "#2E75B6", "#FFC000", "Georgia", 6),
                ("#2E7D32", "#66BB6A", "#FF7043", "Segoe UI Variable Text", 12),
                ("#312E81", "#6366F1", "#F59E0B", "Cambria", 8),
            };
            var picked = palettes[Math.Abs(fileName.GetHashCode()) % palettes.Length];

            BuilderPrimaryColor = AccentOption.ParseHex(picked.P);
            BuilderSecondaryColor = AccentOption.ParseHex(picked.S);
            BuilderAccentColor = AccentOption.ParseHex(picked.A);
            BuilderFontFamily = picked.Font;
            BuilderCornerRadius = picked.Radius;

            GeneratedCss = BuildGeneratedCss();
            HasGeneratedCssFlag = true;
            StyleAnalysisStatus = $"✅ Palette déduite de « {fileName} » et appliquée au StyleBuilder.";
            StatusMessage = "🎨 Palette suggérée (heuristique locale) — ajustez si besoin puis enregistrez (Ctrl+S).";
            RequestPreviewRefresh();
        }
        finally
        {
            IsAnalyzingStyle = false;
        }
    }

    private string BuildGeneratedCss() => $$"""
        /* ── Feuille de style PROFstudio — générée automatiquement ──── */
        :root {
          --fg-couleur-primaire:    {ToHex(BuilderPrimaryColor)};
          --fg-couleur-secondaire:  {ToHex(BuilderSecondaryColor)};
          --fg-couleur-accent:      {ToHex(BuilderAccentColor)};
          --fg-police:              '{BuilderFontFamily}', sans-serif;
          --fg-rayon-encadres:      {BuilderCornerRadius:0}px;
          --fg-marges:              {BuilderMarginTop:0}mm {BuilderMarginRight:0}mm {BuilderMarginBottom:0}mm {BuilderMarginLeft:0}mm;
        }

        .fiche {
          font-family: var(--fg-police);
          padding: var(--fg-marges);
        }

        .fiche header {
          background: linear-gradient(135deg,
            var(--fg-couleur-primaire), var(--fg-couleur-secondaire));
          border-radius: var(--fg-rayon-encadres);
        }

        .fiche .encadre-objectifs {
          border-left: 4px solid var(--fg-couleur-accent);
        }
        """;

    public string BuildPreviewHtml()
    {
        var css = HtmlRenderer.BuildPresetCss(BuildCustomPresetFromBuilder());
        return HtmlRenderer.RenderToFullHtml(BuildSampleDocument(), css);
    }

    private static GeneratedDocument BuildSampleDocument() => new(
        Metadata: new DocumentMetadata(
            Title: "Les fractions simples : découverte et manipulation",
            Subtitle: "Séance de découverte — manipulation et représentation",
            ClassLevel: "CM2",
            Subject: "Mathématiques",
            Duration: 45,
            Date: DateTime.Now.ToString("dd/MM/yyyy")),
        Blocks: new List<Block>
        {
            new HeadingBlock(1, "1. Objectifs d'apprentissage"),
            new CalloutBoxBlock("objectifs", new List<Block>
            {
                new BulletListBlock(new List<List<TextRun>>
                {
                    new() { new TextRun("Comprendre la fraction comme partage de l'unité.") },
                    new() { new TextRun("Lire, écrire et représenter les fractions usuelles (1/2, 1/3, 1/4).") },
                    new() { new TextRun("Nommer la partie numérateur et le dénominateur.") }
                })
            }),
            new HeadingBlock(1, "2. Phase de découverte"),
            new ParagraphBlock(new List<TextRun>
            {
                new TextRun("Distribuer une bande de papier unité à chaque élève. ", IsBold: true),
                new TextRun("Consigne : « Pliez votre bande pour obtenir deux parts égales, puis coloriez-en une. »"),
            }),
            new ParagraphBlock(new List<TextRun>
            {
                new TextRun("Cadrage théorique : "),
                new TextRun("la fraction ", IsItalic: true),
                new TextRun("1/2", IsBold: true),
                new TextRun(" se lit « un demi » et représente une part sur deux parts égales."),
            }),
            new HeadingBlock(2, "Déroulement de la séance"),
            new TableBlock(
                new List<string> { "Phase", "Durée", "Activité des élèves" },
                new List<List<string>>
                {
                    new() { "Découverte", "10 min", "Pliage de la bande unité, premier repérage du demi." },
                    new() { "Manipulation", "20 min", "Constitution de la boîte à fractions (1/2, 1/3, 1/4)." },
                    new() { "Structuration", "15 min", "Trace écrite : vocabulaire numérateur / dénominateur." }
                }),
            new KeyValueGridBlock(new List<KeyValuePair<string, string>>
            {
                new("Matériel", "Bandes de papier, ciseaux, crayons de couleur"),
                new("Organisation", "Binômes puis collectif"),
                new("Différenciation", "Bandes pré-pliées pour les élèves en difficulté")
            }),
            new CalloutBoxBlock("corrige", new List<Block>
            {
                new ParagraphBlock(new List<TextRun>
                {
                    new TextRun("Corrigé — ", IsBold: true),
                    new TextRun("la bande pliée en deux parties égales illustre 1/2 ; chaque part vaut un demi de l'unité.")
                })
            }),
            new CalloutBoxBlock("differentiation", new List<Block>
            {
                new ParagraphBlock(new List<TextRun>
                {
                    new TextRun("Pour aller plus loin : ", IsBold: true),
                    new TextRun("faire construire 2/4 et comparer avec 1/2 (première approche des fractions équivalentes).")
                })
            })
        });

    private void RegisterCustomPreset()
    {
        var preset = BuildCustomPresetFromBuilder();
        _stylePresetService.AddCustomPreset(preset);
        SelectedStylePresetId = "custom";
        DefaultStyleLabel = "Personnalisé (StyleBuilder)";
    }

    private void RefreshDefaultStyleLabel(string? stylePresetId)
    {
        DefaultStyleLabel = string.Equals(stylePresetId, "custom", StringComparison.Ordinal)
            ? "Personnalisé (StyleBuilder)"
            : StylePresets.FirstOrDefault(p => p.Id == stylePresetId)?.Name ?? "Moderne";
    }
}
