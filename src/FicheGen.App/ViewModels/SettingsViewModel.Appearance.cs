using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.App.Services;
using FicheGen.Core.Documents;
using Microsoft.Extensions.DependencyInjection;
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

    partial void OnDefaultSubjectChanged(string value)
    {
        if (_isLoadingSettings) return;
        try
        {
            var settings = _settingsStore.GetSettings<FicheGen.Core.Storage.AppSettings>();
            settings.Defaults.Subject = value;
            _ = _settingsStore.SaveSettingsAsync(settings);
            ApplyDefaultSubjectToForms(value);
        }
        catch { }
    }

    partial void OnDefaultClassLevelChanged(string value)
    {
        if (_isLoadingSettings) return;
        try
        {
            var settings = _settingsStore.GetSettings<FicheGen.Core.Storage.AppSettings>();
            settings.Defaults.ClassLevel = value;
            _ = _settingsStore.SaveSettingsAsync(settings);
            ApplyDefaultClassLevelToForms(value);
        }
        catch { }
    }

    private void ApplyDefaultSubjectToForms(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return;
        try
        {
            var ficheVm = App.Services.GetService<FicheFormViewModel>();
            if (ficheVm != null && !ficheVm.IsGenerating) ficheVm.Subject = subject;
            var evalVm = App.Services.GetService<EvaluationViewModel>();
            if (evalVm != null && !evalVm.IsGenerating) evalVm.Subject = subject;
            var quizVm = App.Services.GetService<QuizViewModel>();
            if (quizVm != null && !quizVm.IsGenerating) quizVm.Subject = subject;
        }
        catch { }
    }

    private void ApplyDefaultClassLevelToForms(string classLevel)
    {
        if (string.IsNullOrWhiteSpace(classLevel)) return;
        try
        {
            var ficheVm = App.Services.GetService<FicheFormViewModel>();
            if (ficheVm != null && !ficheVm.IsGenerating) ficheVm.ClassLevel = classLevel;
            var evalVm = App.Services.GetService<EvaluationViewModel>();
            if (evalVm != null && !evalVm.IsGenerating) evalVm.ClassLevel = classLevel;
            var quizVm = App.Services.GetService<QuizViewModel>();
            if (quizVm != null && !quizVm.IsGenerating) quizVm.ClassLevel = classLevel;
        }
        catch { }
    }

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
        }
    }

    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new()
    {
        new("system", L10n.Get("Theme_System", "Par défaut du système"), "🖥️"),
        new("light", L10n.Get("Theme_Light", "Clair"), "☀️"),
        new("dark", L10n.Get("Theme_Dark", "Sombre"), "🌙"),
        new("oled", L10n.Get("Theme_Oled", "Noir absolu (OLED)"), "⬛"),
    };

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = new List<LanguageOption>
    {
        new("fr-FR", "Français (France)", "🇫🇷"),
        new("en-US", "English (United States)", "🇺🇸"),
        new("ar-SA", "العربية (Arabic)", "🇸🇦"),
    };

    public ObservableCollection<AccentOption> AccentOptions { get; } = new()
    {
        new AccentOption(L10n.Get("SP_PaletteOcean", "Bleu PROFstudio"), "#2563EB"),
        new AccentOption(L10n.Get("SP_PalettePurple", "Violet encre"), "#7C3AED"),
        new AccentOption(L10n.Get("SP_PaletteEmerald", "Vert forêt"), "#059669"),
        new AccentOption(L10n.Get("SP_PaletteTerracotta", "Orange ardoise"), "#EA580C"),
        new AccentOption(L10n.Get("SP_PaletteRuby", "Framboise"), "#DB2777"),
        new AccentOption(L10n.Get("SP_PaletteSlate", "Sarcelle"), "#0D9488"),
    };

    public void RefreshLocalizedOptions()
    {
        var curThemeKey = SelectedThemeOption?.Key ?? Theme;
        ThemeOptions.Clear();
        ThemeOptions.Add(new("system", L10n.Get("Theme_System", "Par défaut du système"), "🖥️"));
        ThemeOptions.Add(new("light", L10n.Get("Theme_Light", "Clair"), "☀️"));
        ThemeOptions.Add(new("dark", L10n.Get("Theme_Dark", "Sombre"), "🌙"));
        ThemeOptions.Add(new("oled", L10n.Get("Theme_Oled", "Noir absolu (OLED)"), "⬛"));
        SelectedThemeOption = ThemeOptions.FirstOrDefault(t => t.Key == curThemeKey) ?? ThemeOptions[0];

        var curAccentHex = SelectedAccent?.Hex;
        AccentOptions.Clear();
        AccentOptions.Add(new AccentOption(L10n.Get("SP_PaletteOcean", "Bleu PROFstudio"), "#2563EB"));
        AccentOptions.Add(new AccentOption(L10n.Get("SP_PalettePurple", "Violet encre"), "#7C3AED"));
        AccentOptions.Add(new AccentOption(L10n.Get("SP_PaletteEmerald", "Vert forêt"), "#059669"));
        AccentOptions.Add(new AccentOption(L10n.Get("SP_PaletteTerracotta", "Orange ardoise"), "#EA580C"));
        AccentOptions.Add(new AccentOption(L10n.Get("SP_PaletteRuby", "Framboise"), "#DB2777"));
        AccentOptions.Add(new AccentOption(L10n.Get("SP_PaletteSlate", "Sarcelle"), "#0D9488"));

        if (!string.IsNullOrEmpty(curAccentHex))
        {
            SelectedAccent = AccentOptions.FirstOrDefault(a => a.Hex.Equals(curAccentHex, StringComparison.OrdinalIgnoreCase)) ?? AccentOptions[0];
        }
        else
        {
            SelectedAccent = AccentOptions[0];
        }
    }

    // ─────────────── Onglet 4 · Styles & Personnalisation (Studio) ───────────────

    public ObservableCollection<StylePresetItem> StylePresets { get; } = new()
    {
        new StylePresetItem("modern", "Moderne", "Édition contemporaine : bleu marine, accents ambre, filet asymétrique.", "🌊", "#1E3A8A", "#2563EB", "#D97706", "Segoe UI Variable Text", 8, StylePreset.HeaderRule),
        new StylePresetItem("classic", "Classique", "Manuel scolaire : bleu nuit, rouge garance, lettrines et serif.", "📘", "#1E3A5F", "#475569", "#991B1B", "Georgia", 3, StylePreset.HeaderCentered),
        new StylePresetItem("minimal", "Minimaliste", "Typographie suisse : noir pur, gris zinc, pastille émeraude.", "⬜", "#0F172A", "#475569", "#059669", "Segoe UI Variable Text", 2, StylePreset.HeaderMinimal),
        new StylePresetItem("academic", "Académique", "Sorbonne & universités : pourpre profond, outremer et bronze.", "🎓", "#311042", "#4338CA", "#B45309", "Cambria", 4, StylePreset.HeaderCentered),
        new StylePresetItem("playful", "Ludique", "Primaire & découverte : bandeau dégradé, formes douces et miel.", "🎈", "#4F46E5", "#DB2777", "#F59E0B", "Trebuchet MS", 14, StylePreset.HeaderBand),
        new StylePresetItem("dyslexie", "Dyslexie", "Lisibilité renforcée : fort contraste, espacements aérés et clarté.", "👁️", "#0F172A", "#1E3A8A", "#0284C7", "Segoe UI Variable Text", 8, StylePreset.HeaderRule),
    };

    [ObservableProperty] public partial StylePresetItem? SelectedPreset { get; set; }
    [ObservableProperty] public partial string SelectedStylePresetId { get; set; } = "modern";
    [ObservableProperty] public partial string DefaultStyleLabel { get; set; } = "Moderne";

    // StyleBuilder (Éditeur de style interactif)
    [ObservableProperty] public partial string BuilderThemeName { get; set; } = "Mon Style";
    [ObservableProperty] public partial string BuilderThemeIcon { get; set; } = "✨";
    [ObservableProperty] public partial bool IsSelectedThemeCustom { get; set; } = false;

    [ObservableProperty] public partial Color BuilderPrimaryColor { get; set; } = AccentOption.ParseHex("#1E3A8A");
    [ObservableProperty] public partial Color BuilderSecondaryColor { get; set; } = AccentOption.ParseHex("#2563EB");
    [ObservableProperty] public partial Color BuilderAccentColor { get; set; } = AccentOption.ParseHex("#D97706");
    [ObservableProperty] public partial string BuilderPrimaryHex { get; set; } = "#1E3A8A";
    [ObservableProperty] public partial string BuilderSecondaryHex { get; set; } = "#2563EB";
    [ObservableProperty] public partial string BuilderAccentHex { get; set; } = "#D97706";
    [ObservableProperty] public partial string BuilderFontFamily { get; set; } = "Segoe UI Variable Text";
    [ObservableProperty] public partial string BuilderHeaderLayout { get; set; } = StylePreset.HeaderRule;
    [ObservableProperty] public partial double BuilderCornerRadius { get; set; } = 8;
    [ObservableProperty] public partial double BuilderMarginTop { get; set; } = 20;
    [ObservableProperty] public partial double BuilderMarginBottom { get; set; } = 20;
    [ObservableProperty] public partial double BuilderMarginLeft { get; set; } = 20;
    [ObservableProperty] public partial double BuilderMarginRight { get; set; } = 20;

    private bool _isLoadingPresetIntoBuilder;

    public IReadOnlyList<HeaderLayoutOption> HeaderLayoutOptions { get; } = new List<HeaderLayoutOption>
    {
        new(StylePreset.HeaderRule, "Filet bicolore", "Titre mis en valeur avec filet décoratif."),
        new(StylePreset.HeaderBand, "Bandeau dégradé", "Bandeau plein coloré avec titre blanc."),
        new(StylePreset.HeaderCentered, "Titre centré académique", "Présentation équilibrée centrée."),
        new(StylePreset.HeaderMinimal, "Minimaliste épuré", "Titre sobre avec pastille d'accentuation."),
    };

    public IReadOnlyList<string> FontOptions { get; } = new List<string>
    {
        "Segoe UI Variable Text", "Segoe UI", "Georgia", "Cambria", "Calibri",
        "Arial", "Verdana", "Trebuchet MS", "Segoe Print",
    };

    public IReadOnlyList<string> QuickEmojiIcons { get; } = new[]
    {
        "✨", "🎨", "📚", "✏️", "🎓", "💡", "🌟", "🔬", "📐", "🎈", "🌊", "📘", "👁️"
    };

    // Suggestions et IA
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
        RefreshLocalizedOptions();

        if (_isLoadingSettings) return;

        try
        {
            var settings = _settingsStore.GetSettings<FicheGen.Core.Storage.AppSettings>();
            settings.Ui.Language = value;
            _ = _settingsStore.SaveSettingsAsync(settings);

            StatusMessage = value switch
            {
                "ar-SA" => "تم تغيير لغة التطبيق إلى العربية — تم تحديث النصوص فورياً.",
                "en-US" => "Language changed to English — interface strings updated.",
                _ => "Langue modifiée en français — textes de l'interface mis à jour."
            };
        }
        catch { }
    }

    // ─────────────── Gestion dynamique des thèmes ───────────────

    [RelayCommand]
    public void CreateNewTheme()
    {
        var id = $"custom_{Guid.NewGuid().ToString("N")[..8]}";
        var count = StylePresets.Count(p => p.IsCustom) + 1;
        var name = $"Mon Thème {count}";
        var newPreset = new StylePresetItem(
            id,
            name,
            "Thème personnalisé sur-mesure",
            "✨",
            ToHex(BuilderPrimaryColor),
            ToHex(BuilderSecondaryColor),
            ToHex(BuilderAccentColor),
            BuilderFontFamily,
            BuilderCornerRadius,
            BuilderHeaderLayout,
            isCustom: true,
            isVisible: true,
            isDefault: false,
            marginTop: BuilderMarginTop,
            marginBottom: BuilderMarginBottom,
            marginLeft: BuilderMarginLeft,
            marginRight: BuilderMarginRight
        );

        StylePresets.Add(newPreset);
        SelectedPreset = newPreset;
        RegisterPresetIntoService(newPreset);
        StatusMessage = $"✨ Thème « {name} » créé avec succès. Modifiez ses réglages puis enregistrez (Ctrl+S).";
    }

    [RelayCommand]
    public void SetAsDefaultTheme(StylePresetItem? item)
    {
        if (item is null) return;
        SelectedStylePresetId = item.Id;
        DefaultStyleLabel = item.Name;

        foreach (var p in StylePresets)
        {
            p.IsDefault = string.Equals(p.Id, item.Id, StringComparison.OrdinalIgnoreCase);
        }

        RegisterPresetIntoService(item);
        StatusMessage = $"⭐ Thème « {item.Name} » défini par défaut pour les futures fiches.";
        RequestPreviewRefresh();
    }

    [RelayCommand]
    public void MoveThemeLeft(StylePresetItem? item)
    {
        if (item is null) return;
        var idx = StylePresets.IndexOf(item);
        if (idx > 0)
        {
            StylePresets.Move(idx, idx - 1);
            StatusMessage = $"Tuile « {item.Name} » déplacée vers la gauche.";
        }
    }

    [RelayCommand]
    public void MoveThemeRight(StylePresetItem? item)
    {
        if (item is null) return;
        var idx = StylePresets.IndexOf(item);
        if (idx >= 0 && idx < StylePresets.Count - 1)
        {
            StylePresets.Move(idx, idx + 1);
            StatusMessage = $"Tuile « {item.Name} » déplacée vers la droite.";
        }
    }

    public void ReorderTheme(StylePresetItem source, StylePresetItem target)
    {
        if (source == null || target == null || ReferenceEquals(source, target)) return;
        var oldIdx = StylePresets.IndexOf(source);
        var newIdx = StylePresets.IndexOf(target);
        if (oldIdx >= 0 && newIdx >= 0)
        {
            StylePresets.Move(oldIdx, newIdx);
            StatusMessage = $"✓ Thème « {source.Name} » repositionné.";
        }
    }

    [RelayCommand]
    public void ToggleThemeVisibility(StylePresetItem? item)
    {
        if (item is null) return;
        item.IsVisible = !item.IsVisible;
        StatusMessage = item.IsVisible
            ? $"Tuile « {item.Name} » visible."
            : $"Tuile « {item.Name} » masquée.";
    }

    [RelayCommand]
    public void DuplicateTheme(StylePresetItem? item)
    {
        if (item is null) return;
        var id = $"custom_{Guid.NewGuid().ToString("N")[..8]}";
        var newPreset = new StylePresetItem(
            id,
            $"{item.Name} (Copie)",
            item.Description,
            item.Icon,
            item.PrimaryHex,
            item.SecondaryHex,
            item.AccentHex,
            item.FontFamily,
            item.CornerRadius,
            item.HeaderLayout,
            isCustom: true,
            isVisible: true,
            isDefault: false,
            marginTop: item.MarginTop,
            marginBottom: item.MarginBottom,
            marginLeft: item.MarginLeft,
            marginRight: item.MarginRight
        );

        var idx = StylePresets.IndexOf(item);
        if (idx >= 0) StylePresets.Insert(idx + 1, newPreset);
        else StylePresets.Add(newPreset);

        SelectedPreset = newPreset;
        RegisterPresetIntoService(newPreset);
        StatusMessage = $"📋 Thème dupliqué en « {newPreset.Name} ».";
    }

    [RelayCommand]
    public void DeleteTheme(StylePresetItem? item)
    {
        if (item is null || !item.IsCustom) return;
        var wasSelected = SelectedPreset == item;
        var wasDefault = item.IsDefault;

        StylePresets.Remove(item);

        if (wasSelected)
        {
            SelectedPreset = StylePresets.FirstOrDefault(p => p.IsVisible) ?? StylePresets.FirstOrDefault();
        }

        if (wasDefault)
        {
            var fallback = StylePresets.FirstOrDefault();
            if (fallback != null)
            {
                SetAsDefaultTheme(fallback);
            }
        }

        StatusMessage = $"🗑️ Thème « {item.Name} » supprimé.";
    }

    [RelayCommand]
    public void ResetThemesToDefault()
    {
        StylePresets.Clear();
        StylePresets.Add(new StylePresetItem("modern", "Moderne", "Édition contemporaine : bleu marine, accents ambre, filet asymétrique.", "🌊", "#1E3A8A", "#2563EB", "#D97706", "Segoe UI Variable Text", 8, StylePreset.HeaderRule, isDefault: true));
        StylePresets.Add(new StylePresetItem("classic", "Classique", "Manuel scolaire : bleu nuit, rouge garance, lettrines et serif.", "📘", "#1E3A5F", "#475569", "#991B1B", "Georgia", 3, StylePreset.HeaderCentered));
        StylePresets.Add(new StylePresetItem("minimal", "Minimaliste", "Typographie suisse : noir pur, gris zinc, pastille émeraude.", "⬜", "#0F172A", "#475569", "#059669", "Segoe UI Variable Text", 2, StylePreset.HeaderMinimal));
        StylePresets.Add(new StylePresetItem("academic", "Académique", "Sorbonne & universités : pourpre profond, outremer et bronze.", "🎓", "#311042", "#4338CA", "#B45309", "Cambria", 4, StylePreset.HeaderCentered));
        StylePresets.Add(new StylePresetItem("playful", "Ludique", "Primaire & découverte : bandeau dégradé, formes douces et miel.", "🎈", "#4F46E5", "#DB2777", "#F59E0B", "Trebuchet MS", 14, StylePreset.HeaderBand));
        StylePresets.Add(new StylePresetItem("dyslexie", "Dyslexie", "Lisibilité renforcée : fort contraste, espacements aérés et clarté.", "👁️", "#0F172A", "#1E3A8A", "#0284C7", "Segoe UI Variable Text", 8, StylePreset.HeaderRule));

        SelectedPreset = StylePresets[0];
        SetAsDefaultTheme(StylePresets[0]);
        StatusMessage = "↺ Galerie de thèmes rétablie aux réglages d'usine.";
    }

    // ─────────────── Liaison StyleBuilder ↔ Thème sélectionné ───────────────

    partial void OnSelectedPresetChanged(StylePresetItem? value)
    {
        if (value is null) return;

        _isLoadingPresetIntoBuilder = true;
        BuilderThemeName = value.Name;
        BuilderThemeIcon = value.Icon;
        IsSelectedThemeCustom = value.IsCustom;

        BuilderPrimaryColor = AccentOption.ParseHex(value.PrimaryHex);
        BuilderSecondaryColor = AccentOption.ParseHex(value.SecondaryHex);
        BuilderAccentColor = AccentOption.ParseHex(value.AccentHex);
        BuilderPrimaryHex = value.PrimaryHex;
        BuilderSecondaryHex = value.SecondaryHex;
        BuilderAccentHex = value.AccentHex;
        BuilderFontFamily = value.FontFamily;
        BuilderHeaderLayout = value.HeaderLayout;
        BuilderCornerRadius = value.CornerRadius;
        BuilderMarginTop = value.MarginTop;
        BuilderMarginBottom = value.MarginBottom;
        BuilderMarginLeft = value.MarginLeft;
        BuilderMarginRight = value.MarginRight;

        _isLoadingPresetIntoBuilder = false;
        RequestPreviewRefresh();
    }

    partial void OnBuilderThemeNameChanged(string value)
    {
        if (_isLoadingPresetIntoBuilder || SelectedPreset == null || !SelectedPreset.IsCustom) return;
        SelectedPreset.Name = value;
        if (SelectedPreset.IsDefault) DefaultStyleLabel = value;
    }

    partial void OnBuilderThemeIconChanged(string value)
    {
        if (_isLoadingPresetIntoBuilder || SelectedPreset == null || !SelectedPreset.IsCustom) return;
        SelectedPreset.Icon = value;
    }

    [RelayCommand]
    public void SelectBuilderIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return;
        BuilderThemeIcon = icon;
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
            "pastel" => ("#1E40AF", "#3B82F6", "#EC4899"),
            "mono" => ("#111827", "#374151", "#6B7280"),
            _ => ("#1E3A8A", "#2563EB", "#D97706")
        };

        BuilderPrimaryColor = AccentOption.ParseHex(p);
        BuilderSecondaryColor = AccentOption.ParseHex(s);
        BuilderAccentColor = AccentOption.ParseHex(a);
    }

    [RelayCommand]
    public void ApplyQuickMargins(string? mode)
    {
        var (top, bottom, left, right) = mode switch
        {
            "compact" => (12, 12, 14, 14),
            "wide" => (26, 26, 26, 26),
            _ => (20, 20, 20, 20)
        };
        BuilderMarginTop = top;
        BuilderMarginBottom = bottom;
        BuilderMarginLeft = left;
        BuilderMarginRight = right;
    }

    [RelayCommand]
    public void ApplyQuickCornerRadius(double radius)
    {
        BuilderCornerRadius = Math.Clamp(radius, 0, 24);
    }

    private void SyncBuilderToSelectedPreset()
    {
        if (_isLoadingPresetIntoBuilder || _isLoadingSettings) return;

        if (SelectedPreset != null && SelectedPreset.IsCustom)
        {
            SelectedPreset.PrimaryHex = ToHex(BuilderPrimaryColor);
            SelectedPreset.SecondaryHex = ToHex(BuilderSecondaryColor);
            SelectedPreset.AccentHex = ToHex(BuilderAccentColor);
            SelectedPreset.FontFamily = BuilderFontFamily;
            SelectedPreset.CornerRadius = BuilderCornerRadius;
            SelectedPreset.HeaderLayout = BuilderHeaderLayout;
            SelectedPreset.MarginTop = BuilderMarginTop;
            SelectedPreset.MarginBottom = BuilderMarginBottom;
            SelectedPreset.MarginLeft = BuilderMarginLeft;
            SelectedPreset.MarginRight = BuilderMarginRight;
            RegisterPresetIntoService(SelectedPreset);
        }

        RequestPreviewRefresh();
    }

    partial void OnBuilderPrimaryColorChanged(Color value)
    {
        BuilderPrimaryHex = ToHex(value);
        SyncBuilderToSelectedPreset();
    }

    partial void OnBuilderSecondaryColorChanged(Color value)
    {
        BuilderSecondaryHex = ToHex(value);
        SyncBuilderToSelectedPreset();
    }

    partial void OnBuilderAccentColorChanged(Color value)
    {
        BuilderAccentHex = ToHex(value);
        SyncBuilderToSelectedPreset();
    }

    partial void OnBuilderFontFamilyChanged(string value) => SyncBuilderToSelectedPreset();
    partial void OnBuilderHeaderLayoutChanged(string value) => SyncBuilderToSelectedPreset();
    partial void OnBuilderCornerRadiusChanged(double value) => SyncBuilderToSelectedPreset();
    partial void OnBuilderMarginTopChanged(double value) => SyncBuilderToSelectedPreset();
    partial void OnBuilderMarginBottomChanged(double value) => SyncBuilderToSelectedPreset();
    partial void OnBuilderMarginLeftChanged(double value) => SyncBuilderToSelectedPreset();
    partial void OnBuilderMarginRightChanged(double value) => SyncBuilderToSelectedPreset();

    private void RequestPreviewRefresh() => PreviewRefreshRequested?.Invoke(this, EventArgs.Empty);

    // ─────────────── Service & Rendu ───────────────

    public StylePreset BuildCustomPresetFromBuilder() => new()
    {
        Id = SelectedPreset?.Id ?? "custom",
        Name = SelectedPreset?.Name ?? "Personnalisé",
        PrimaryColor = ToHex(BuilderPrimaryColor),
        SecondaryColor = ToHex(BuilderSecondaryColor),
        AccentColor = ToHex(BuilderAccentColor),
        FontFamily = string.IsNullOrWhiteSpace(BuilderFontFamily) ? "Segoe UI" : BuilderFontFamily,
        MarginMm = (int)Math.Round(BuilderMarginTop),
        MarginBottomMm = (int)Math.Round(BuilderMarginBottom),
        MarginLeftMm = (int)Math.Round(BuilderMarginLeft),
        MarginRightMm = (int)Math.Round(BuilderMarginRight),
        CornerRadiusPx = BuilderCornerRadius,
        HeaderLayout = string.IsNullOrWhiteSpace(BuilderHeaderLayout) ? StylePreset.HeaderRule : BuilderHeaderLayout
    };

    private void RegisterPresetIntoService(StylePresetItem item)
    {
        var preset = new StylePreset
        {
            Id = item.Id,
            Name = item.Name,
            PrimaryColor = item.PrimaryHex,
            SecondaryColor = item.SecondaryHex,
            AccentColor = item.AccentHex,
            FontFamily = string.IsNullOrWhiteSpace(item.FontFamily) ? "Segoe UI" : item.FontFamily,
            MarginMm = (int)Math.Round(item.MarginTop),
            MarginBottomMm = (int)Math.Round(item.MarginBottom),
            MarginLeftMm = (int)Math.Round(item.MarginLeft),
            MarginRightMm = (int)Math.Round(item.MarginRight),
            CornerRadiusPx = item.CornerRadius,
            HeaderLayout = string.IsNullOrWhiteSpace(item.HeaderLayout) ? StylePreset.HeaderRule : item.HeaderLayout
        };
        _stylePresetService.AddCustomPreset(preset);
    }

    public string BuildPreviewHtml()
    {
        var preset = BuildCustomPresetFromBuilder();
        var css = HtmlRenderer.BuildPresetCss(preset);
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

    public void RefreshDefaultStyleLabel(string? stylePresetId)
    {
        var matched = StylePresets.FirstOrDefault(p => string.Equals(p.Id, stylePresetId, StringComparison.OrdinalIgnoreCase));
        DefaultStyleLabel = matched?.Name ?? "Moderne";
        foreach (var p in StylePresets)
        {
            p.IsDefault = string.Equals(p.Id, stylePresetId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task AnalyzeStylePdfAsync(string fileName)
    {
        IsAnalyzingStyle = true;
        HasGeneratedCssFlag = false;

        try
        {
            StyleAnalysisStatus = $"🎨 Application de la palette suggérée…";
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
            StyleAnalysisStatus = $"✅ Palette suggérée appliquée au StyleBuilder.";
            StatusMessage = "🎨 Palette suggérée appliquée — ajustez les couleurs si besoin puis enregistrez (Ctrl+S).";
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

    // ─────────────── Sérialisation & Persistance ───────────────

    public string GetCustomPresetsJson()
    {
        var custom = StylePresets.Where(p => p.IsCustom).Select(p => new CustomPresetDto(
            p.Id, p.Name, p.Description, p.Icon, p.PrimaryHex, p.SecondaryHex, p.AccentHex,
            p.FontFamily, p.CornerRadius, p.HeaderLayout, p.IsVisible, p.IsCustom,
            p.MarginTop, p.MarginBottom, p.MarginLeft, p.MarginRight)).ToList();
        return JsonSerializer.Serialize(custom);
    }

    public string GetPresetsOrderJson()
    {
        var order = StylePresets.Select(p => p.Id).ToList();
        return JsonSerializer.Serialize(order);
    }

    public void LoadCustomPresetsFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<CustomPresetDto>>(json);
            if (list == null) return;

            foreach (var dto in list)
            {
                if (StylePresets.Any(p => p.Id.Equals(dto.Id, StringComparison.OrdinalIgnoreCase))) continue;

                var item = new StylePresetItem(
                    dto.Id, dto.Name, dto.Description, dto.Icon,
                    dto.PrimaryHex, dto.SecondaryHex, dto.AccentHex,
                    dto.FontFamily, dto.CornerRadius, dto.HeaderLayout,
                    dto.IsCustom, dto.IsVisible, false,
                    dto.MarginTop, dto.MarginBottom, dto.MarginLeft, dto.MarginRight);

                StylePresets.Add(item);
                RegisterPresetIntoService(item);
            }
        }
        catch { }
    }

    public void ApplyPresetsOrderFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var order = JsonSerializer.Deserialize<List<string>>(json);
            if (order == null || order.Count == 0) return;

            var map = StylePresets.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            var reordered = new List<StylePresetItem>();

            foreach (var id in order)
            {
                if (map.TryGetValue(id, out var item))
                {
                    reordered.Add(item);
                    map.Remove(id);
                }
            }
            reordered.AddRange(map.Values);

            StylePresets.Clear();
            foreach (var item in reordered) StylePresets.Add(item);
        }
        catch { }
    }
}
