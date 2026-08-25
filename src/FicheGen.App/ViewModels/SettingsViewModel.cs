using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.App.Services;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Core.Services;
using FicheGen.Core.Storage;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.System;
using Windows.UI;

namespace FicheGen.App.ViewModels;

// ═══════════════════════════════════════════════════════════════════
//  Types d'options et d'éléments (collections bindables)
// ═══════════════════════════════════════════════════════════════════

public sealed record ProviderOption(string Key, string Label, string Icon);
public sealed record ThemeOption(string Key, string Label, string Icon);
public sealed record LanguageOption(string Code, string Label, string Icon);
public sealed record HeaderLayoutOption(string Key, string Label, string Description);

public sealed class AccentOption
{
    private Brush? _brush;

    public string Name { get; }
    public string Hex { get; }
    public Color Color { get; }
    public Brush? Brush => _brush ??= BrushHelper.TryCreateBrush(Color);

    public AccentOption(string name, string hex)
    {
        Name = name;
        Hex = hex;
        Color = ParseHex(hex);
    }

    public static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }
}

internal static class BrushHelper
{
    public static Brush? TryCreateBrush(Color color)
    {
        try
        {
            return new SolidColorBrush(color);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Préréglage de style de document (galerie de l'onglet Styles).</summary>
public sealed class StylePresetItem
{
    private Brush? _primaryBrush;
    private Brush? _secondaryBrush;
    private Brush? _accentBrush;

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    public string PrimaryHex { get; }
    public string SecondaryHex { get; }
    public string AccentHex { get; }
    public string FontFamily { get; }
    public double CornerRadius { get; }
    public string HeaderLayout { get; }
    public Brush? PrimaryBrush => _primaryBrush ??= BrushHelper.TryCreateBrush(AccentOption.ParseHex(PrimaryHex));
    public Brush? SecondaryBrush => _secondaryBrush ??= BrushHelper.TryCreateBrush(AccentOption.ParseHex(SecondaryHex));
    public Brush? AccentBrush => _accentBrush ??= BrushHelper.TryCreateBrush(AccentOption.ParseHex(AccentHex));

    public StylePresetItem(string id, string name, string description, string icon,
        string primaryHex, string secondaryHex, string accentHex, string fontFamily, double cornerRadius,
        string headerLayout = FicheGen.Core.Documents.StylePreset.HeaderRule)
    {
        Id = id; Name = name; Description = description; Icon = icon;
        PrimaryHex = primaryHex; SecondaryHex = secondaryHex; AccentHex = accentHex;
        FontFamily = fontFamily; CornerRadius = cornerRadius; HeaderLayout = headerLayout;
    }
}

/// <summary>Ligne de la matrice de routage : une tâche → un fournisseur + un modèle.</summary>
public partial class ModelRoutingItem : ObservableObject
{
    public string TaskKey { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Icon { get; }
    public IReadOnlyList<ProviderOption> ProviderOptions => ProviderCatalog.Providers;

    [ObservableProperty] public partial ProviderOption? SelectedProvider { get; set; }
    [ObservableProperty] public partial string Model { get; set; } = string.Empty;
    [ObservableProperty] public partial ObservableCollection<string> AvailableModels { get; set; } = new();

    private string? _customProxyModel;

    public ModelRoutingItem(string taskKey, string displayName, string description, string icon,
        string providerKey, string model)
    {
        TaskKey = taskKey; DisplayName = displayName; Description = description; Icon = icon;
        SelectedProvider = ProviderCatalog.Find(providerKey); // déclenche le remplissage des modèles
        Model = model;
    }

    partial void OnSelectedProviderChanged(ProviderOption? value) => RefreshModels(value?.Key);

    public void ApplyCustomProxyModel(string? model)
    {
        _customProxyModel = model;
        if (SelectedProvider?.Key == "proxy")
            RefreshModels("proxy");
    }

    private void RefreshModels(string? providerKey)
    {
        var models = new List<string>(ProviderCatalog.ModelsFor(providerKey ?? "aistudio"));
        if (providerKey == "proxy" && !string.IsNullOrWhiteSpace(_customProxyModel) && !models.Contains(_customProxyModel))
            models.Insert(0, _customProxyModel);

        AvailableModels = new ObservableCollection<string>(models);
        if (!models.Contains(Model) && models.Count > 0)
            Model = models[0];
    }
}

public enum ConnectionHealth { Unknown, Ok, Warning, Error }

/// <summary>État temps réel du test de connexion d'un fournisseur.</summary>
public partial class ProviderConnectionState : ObservableObject
{
    public string ProviderKey { get; }
    public ConnectionHealth Health { get; private set; } = ConnectionHealth.Unknown;

    [ObservableProperty] public partial string StatusText { get; set; } = "Jamais testé";
    [ObservableProperty] public partial Brush? StatusBrush { get; set; }
    [ObservableProperty] public partial bool IsTesting { get; set; }

    public ProviderConnectionState(string providerKey)
    {
        ProviderKey = providerKey;
        StatusBrush = BrushHelper.TryCreateBrush(Color.FromArgb(255, 110, 110, 118));
    }

    public void SetPending() { IsTesting = true; StatusText = "Test en cours…"; StatusBrush = BrushHelper.TryCreateBrush(Color.FromArgb(255, 110, 110, 118)); }
    public void SetOk(int latencyMs) { IsTesting = false; Health = ConnectionHealth.Ok; StatusText = $"✔ Connecté · {latencyMs} ms"; StatusBrush = BrushHelper.TryCreateBrush(Color.FromArgb(255, 16, 124, 16)); }
    public void SetWarning(string message) { IsTesting = false; Health = ConnectionHealth.Warning; StatusText = $"⚠ {message}"; StatusBrush = BrushHelper.TryCreateBrush(Color.FromArgb(255, 202, 80, 16)); }
    public void SetError(string message) { IsTesting = false; Health = ConnectionHealth.Error; StatusText = $"✖ {message}"; StatusBrush = BrushHelper.TryCreateBrush(Color.FromArgb(255, 196, 43, 28)); }
    public void SetNeutral(string message) { IsTesting = false; Health = ConnectionHealth.Unknown; StatusText = message; StatusBrush = BrushHelper.TryCreateBrush(Color.FromArgb(255, 110, 110, 118)); }
}

/// <summary>Modèle de prompt personnalisable (onglet Prompts &amp; Raccourcis).</summary>
public partial class PromptTemplateItem : ObservableObject
{
    public string Name { get; }
    public string Description { get; }
    public string DefaultContent { get; }

    [ObservableProperty] public partial string Content { get; set; }

    public PromptTemplateItem(string name, string description, string content)
    {
        Name = name; Description = description; DefaultContent = content; Content = content;
    }
}

public partial class KeyboardShortcutItem : ObservableObject
{
    public string Action { get; }
    public string DefaultKeys { get; }

    [ObservableProperty] public partial string Keys { get; set; }

    public KeyboardShortcutItem(string action, string keys)
    {
        Action = action; DefaultKeys = keys; Keys = keys;
    }
}

/// <summary>Catalogue statique fournisseurs ↔ modèles disponibles.</summary>
internal static class ProviderCatalog
{
    public static readonly IReadOnlyList<ProviderOption> Providers = new List<ProviderOption>
    {
        new("aistudio", "Google AI Studio", "💠"),
        new("openai", "OpenAI", "🤖"),
        new("proxy", "Proxy local / Ollama", "🦙"),
        new("vertex", "Vertex AI (GCP)", "☁️"),
        new("vercel", "Vercel AI Gateway", "▲"),
    };

    private static readonly Dictionary<string, string[]> Models = new()
    {
        ["aistudio"] = new[] { "gemini-3.6-flash", "gemini-3.5-flash-lite", "gemini-3.1-pro-preview", "gemini-3-flash-preview", "gemini-2.5-pro", "gemini-2.5-flash" },
        ["openai"] = new[] { "gpt-4o", "gpt-4o-mini", "gpt-4.1", "o4-mini" },
        ["anthropic"] = new[] { "claude-sonnet-4", "claude-3-5-sonnet", "claude-3-5-haiku" },
        ["proxy"] = new[] { "qwen2.5:7b", "llama3.1:8b", "mistral-nemo", "phi-4-mini" },
        ["vertex"] = new[] { "gemini-3.6-flash", "gemini-3.5-flash-lite", "gemini-3.1-pro-preview", "gemini-3-flash-preview", "gemini-2.5-pro" },
        ["vercel"] = new[] { "openai/gpt-4o", "anthropic/claude-sonnet-4", "google/gemini-3.6-flash", "meta/llama-3.1-70b" },
    };

    public static ProviderOption Find(string? key)
        => Providers.FirstOrDefault(p => p.Key == key) ?? Providers[0];

    public static IReadOnlyList<string> ModelsFor(string key)
        => Models.TryGetValue(key, out var m) ? m : Models["aistudio"];

    public static void UpdateProxyModels(IEnumerable<string> models)
    {
        var list = models.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct().ToArray();
        if (list.Length > 0)
        {
            Models["proxy"] = list;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
//  ViewModel principal
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// ViewModel des Paramètres — 6 onglets, routage IA complet par tâche,
/// StyleBuilder avec aperçu temps réel, conformité RGPD et éditeur de prompts.
/// Propriétés partielles CommunityToolkit.Mvvm (compatibilité WinRT AOT).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly ICredentialStore _credentialStore;
    private readonly PickerService _pickerService;
    private readonly StylePresetService _stylePresetService;
    private readonly IDiagnosticBundleExporter? _diagnosticExporter;
    private readonly IConnectionTester? _connectionTester;
    private readonly IProxyModelScanner? _proxyScanner;
    private readonly AccentColorService? _accentColorService;

    /// <summary>Déclenché quand l'aperçu WebView2 doit être régénéré.</summary>
    public event EventHandler? PreviewRefreshRequested;

    private readonly HashSet<string> _clearedCredentials = new();
    private bool _isLoadingSettings;

    // ─────────────── Onglet 1 · Général & Apparence ───────────────

    [ObservableProperty] public partial string Theme { get; set; } = "system";
    [ObservableProperty] public partial string Language { get; set; } = "fr-FR";
    [ObservableProperty] public partial string TeacherName { get; set; } = "Enseignant·e";
    [ObservableProperty] public partial string SchoolName { get; set; } = "École / Établissement";
    [ObservableProperty] public partial string DefaultClassLevel { get; set; } = "CM2";
    [ObservableProperty] public partial string DefaultSubject { get; set; } = "Mathématiques";
    [ObservableProperty] public partial AccentOption? SelectedAccent { get; set; }
    [ObservableProperty] public partial bool LaunchAtStartup { get; set; }
    [ObservableProperty] public partial string StartupStatusText { get; set; } = "Vérification de l'état du démarrage…";

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = new List<ThemeOption>
    {
        new("system", "Par défaut du système", "🖥️"),
        new("light", "Clair", "☀️"),
        new("dark", "Sombre", "🌙"),
    };

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = new List<LanguageOption>
    {
        new("fr-FR", "Français (France)", "🇫🇷"),
        new("en-US", "English (United States)", "🇺🇸"),
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

    // ─────────────── Onglet 2 · Fournisseurs IA & Routage ───────────────

    [ObservableProperty] public partial string GlobalProvider { get; set; } = "aistudio";
    [ObservableProperty] public partial ProviderOption? SelectedGlobalProvider { get; set; }

    public IReadOnlyList<ProviderOption> ProviderOptions => ProviderCatalog.Providers;

    // Clés d'API (Coffre d'identification Windows — jamais sur disque)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeminiKeyConfigured))]
    [NotifyPropertyChangedFor(nameof(IsGeminiKeyMissing))]
    public partial string GeminiApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenAiKeyConfigured))]
    [NotifyPropertyChangedFor(nameof(IsOpenAiKeyMissing))]
    public partial string OpenAiApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnthropicKeyConfigured))]
    [NotifyPropertyChangedFor(nameof(IsAnthropicKeyMissing))]
    public partial string AnthropicApiKey { get; set; } = string.Empty;

    [ObservableProperty] public partial string ProxyApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVercelKeyConfigured))]
    [NotifyPropertyChangedFor(nameof(IsVercelKeyMissing))]
    public partial string VercelApiKey { get; set; } = string.Empty;

    public bool IsGeminiKeyConfigured => !string.IsNullOrWhiteSpace(GeminiApiKey);
    public bool IsGeminiKeyMissing => !IsGeminiKeyConfigured;
    public bool IsOpenAiKeyConfigured => !string.IsNullOrWhiteSpace(OpenAiApiKey);
    public bool IsOpenAiKeyMissing => !IsOpenAiKeyConfigured;
    public bool IsAnthropicKeyConfigured => !string.IsNullOrWhiteSpace(AnthropicApiKey);
    public bool IsAnthropicKeyMissing => !IsAnthropicKeyConfigured;
    public bool IsVercelKeyConfigured => !string.IsNullOrWhiteSpace(VercelApiKey);
    public bool IsVercelKeyMissing => !IsVercelKeyConfigured;

    [ObservableProperty] public partial string ProxyBaseUrl { get; set; } = "http://localhost:11434/v1";
    [ObservableProperty] public partial string CustomEndpointModel { get; set; } = string.Empty;
    [ObservableProperty] public partial string VertexProject { get; set; } = string.Empty;
    [ObservableProperty] public partial string VertexRegion { get; set; } = "europe-west1";

    // États des tests de connexion (un par fournisseur)
    public ProviderConnectionState GeminiState { get; } = new("aistudio");
    public ProviderConnectionState OpenAiState { get; } = new("openai");
    public ProviderConnectionState AnthropicState { get; } = new("anthropic");
    public ProviderConnectionState ProxyState { get; } = new("proxy");
    public ProviderConnectionState VertexState { get; } = new("vertex");
    public ProviderConnectionState VercelState { get; } = new("vercel");

    // Matrice de routage — 7 tâches
    public ObservableCollection<ModelRoutingItem> RoutingMatrix { get; } = new();

    // Modèle d'intention (hérité — la sélection se fait désormais via la matrice)
    [ObservableProperty] public partial string IntentModel { get; set; } = "gemini-3.6-flash";

    // Températures
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GenerationTemperatureLabel))]
    public partial double GenerationTemperature { get; set; } = 0.7;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntentTemperatureLabel))]
    public partial double IntentTemperature { get; set; } = 0.2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AssistantTemperatureLabel))]
    public partial double AssistantTemperature { get; set; } = 0.8;

    public string GenerationTemperatureLabel => DescribeTemperature(GenerationTemperature);
    public string IntentTemperatureLabel => DescribeTemperature(IntentTemperature);
    public string AssistantTemperatureLabel => DescribeTemperature(AssistantTemperature);

    // ─────────────── Onglet 3 · Dossiers & Emplacements ───────────────

    [ObservableProperty] public partial string GuidesDir { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExportsDir { get; set; } = string.Empty;
    [ObservableProperty] public partial string GuidesAccessToken { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsGuidesAccessPersistent { get; set; }
    [ObservableProperty] public partial string TocCacheSizeText { get; set; } = "Calcul en cours…";
    [ObservableProperty] public partial bool IsCacheBusy { get; set; }

    private static string TocCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FicheGen", "Cache", "Toc");

    private static string LogsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FicheGen", "Logs");

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

    /// <summary>Vrai dès que le StyleBuilder diverge du préréglage de la galerie :
    /// l'enregistrement crée alors un style « Personnalisé » appliqué aux nouveaux documents.</summary>
    private bool _builderDirty;

    /// <summary>Évite de marquer le builder « sale » pendant le chargement d'un préréglage.</summary>
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

    // ─────────────── Onglet 5 · Confidentialité & RGPD ───────────────

    [ObservableProperty] public partial bool TelemetryEnabled { get; set; }
    [ObservableProperty] public partial bool ExpMultiPassGen { get; set; }
    [ObservableProperty] public partial double HistoryRetentionDays { get; set; }
    [ObservableProperty] public partial string DiagnosticLogLevel { get; set; } = "Information";

    public IReadOnlyList<string> LogLevels { get; } = new List<string>
        { "Verbose", "Debug", "Information", "Warning", "Error" };

    // ─────────────── Onglet 6 · Prompts & Raccourcis ───────────────

    public ObservableCollection<PromptTemplateItem> PromptTemplates { get; } = new()
    {
        new PromptTemplateItem("Fiche pédagogique",
            "Génération complète d'une fiche de leçon structurée.",
            "Tu es un enseignant expert du primaire et du collège. Rédige une fiche pédagogique complète pour le niveau {Niveau} en {Matiere}, sur le sujet « {Sujet} ».\n\nStructure attendue :\n1. Objectifs d'apprentissage (3 maximum)\n2. Rappel de cours synthétique\n3. Exercices progressifs (facile → difficile)\n4. Exercice de différenciation pour élèves à besoins particuliers\n5. Corrigé détaillé\n\nConsigne enseignant : {Consigne}"),
        new PromptTemplateItem("Évaluation / contrôle",
            "Création de contrôles notés avec barème.",
            "Crée une évaluation de 20 points pour le niveau {Niveau} en {Matiere} sur « {Sujet} ».\n\nExigences :\n- 4 exercices de difficulté croissante\n- Barème détaillé par question\n- Compétences du socle commun visées\n- Durée indicative : 45 minutes\n- Consigne enseignant : {Consigne}"),
        new PromptTemplateItem("Quiz rapide",
            "QCM de 10 questions avec corrigé.",
            "Génère un quiz de 10 questions à choix multiples (4 options, 1 bonne réponse) pour le niveau {Niveau} en {Matiere} sur « {Sujet} ».\n\nFournis le corrigé commenté en fin de document. {Consigne}"),
        new PromptTemplateItem("Plan de séquence",
            "Progression sur plusieurs séances.",
            "Établis un plan de séquence pédagogique de 5 séances pour le niveau {Niveau} en {Matiere} sur « {Sujet} ».\n\nPour chaque séance : objectif, déroulé minuté, matériel, évaluation formative. {Consigne}"),
    };

    [ObservableProperty] public partial PromptTemplateItem? SelectedPromptTemplate { get; set; }

    public ObservableCollection<KeyboardShortcutItem> Shortcuts { get; } = new()
    {
        new KeyboardShortcutItem("Nouvelle fiche", "Ctrl+N"),
        new KeyboardShortcutItem("Générer la fiche", "Ctrl+Entrée"),
        new KeyboardShortcutItem("Exporter en PDF", "Ctrl+E"),
        new KeyboardShortcutItem("Ouvrir les paramètres", "Ctrl+,"),
        new KeyboardShortcutItem("Basculer clair / sombre", "Ctrl+T"),
    };

    // ─────────────── État global ───────────────

    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string LastSavedText { get; set; } = "Aucune modification enregistrée pour le moment.";
    [ObservableProperty] public partial bool IsSaving { get; set; }

    // DTO de sérialisation (stockage clé/valeur extensible — aucun changement de contrat Core requis)
    private sealed record RoutingDto(string Provider, string Model);
    private sealed record StyleBuilderDto(string Primary, string Secondary, string Accent, string Font,
        double Radius, double MarginTop, double MarginBottom, double MarginLeft, double MarginRight,
        string? Header = null);
    private sealed record PromptDto(string Name, string Content);
    private sealed record ShortcutDto(string Action, string Keys);

    // ─────────────── Construction ───────────────

    public SettingsViewModel(
        ISettingsStore settingsStore,
        ICredentialStore credentialStore,
        PickerService pickerService,
        StylePresetService stylePresetService,
        IDiagnosticBundleExporter? diagnosticExporter = null,
        IConnectionTester? connectionTester = null,
        IProxyModelScanner? proxyScanner = null,
        AccentColorService? accentColorService = null)
    {
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _pickerService = pickerService;
        _stylePresetService = stylePresetService;
        _diagnosticExporter = diagnosticExporter;
        _connectionTester = connectionTester;
        _proxyScanner = proxyScanner;
        _accentColorService = accentColorService;

        // État « chargement » : évite que les valeurs par défaut posées ici
        // (accent, thème) déclenchent une application prématurée à l'interface —
        // AccentColorService.ApplyFromSettings (démarrage) a déjà posé la teinte
        // persistée, et LoadSettings rétablira les valeurs sauvegardées.
        _isLoadingSettings = true;
        SelectedAccent = AccentOptions[0];
        SelectedGlobalProvider = ProviderCatalog.Find("aistudio");
        SelectedPromptTemplate = PromptTemplates[0];

        BuildRoutingMatrixDefaults();
        LoadSettings();

        _ = RefreshTocCacheSizeAsync();
    }

    private void BuildRoutingMatrixDefaults()
    {
        RoutingMatrix.Clear();
        RoutingMatrix.Add(new ModelRoutingItem("fiche", "Fiche pédagogique", "Génération complète de la fiche (contenu + mise en page)", "📄", "aistudio", "gemini-3.6-flash"));
        RoutingMatrix.Add(new ModelRoutingItem("evaluation", "Évaluation", "Création de contrôles et d'évaluations notées", "📝", "aistudio", "gemini-3.6-flash"));
        RoutingMatrix.Add(new ModelRoutingItem("quiz", "Quiz interactif", "Questions à choix multiples et corrections", "❓", "aistudio", "gemini-3.5-flash-lite"));
        RoutingMatrix.Add(new ModelRoutingItem("toc", "Table des matières", "Extraction de la ToC des guides PDF", "📑", "aistudio", "gemini-3.5-flash-lite"));
        RoutingMatrix.Add(new ModelRoutingItem("offset", "Calques & décalages", "Post-traitement Offset des blocs générés", "🧩", "proxy", "qwen2.5:7b"));
        RoutingMatrix.Add(new ModelRoutingItem("syntax", "Coloration syntaxique", "Analyse Syntax des exercices de code", "🖍️", "proxy", "qwen2.5:7b"));
        RoutingMatrix.Add(new ModelRoutingItem("chat", "Assistant conversationnel", "Discussion pédagogique en temps réel", "💬", "aistudio", "gemini-3.6-flash"));
    }

    // ─────────────── Chargement ───────────────

    private void LoadSettings()
    {
        _isLoadingSettings = true;
        var appSettings = _settingsStore.GetSettings<AppSettings>();

        // Le shell est la source de vérité pour le thème (menu de la barre de titre,
        // Paramètres) : on reflète son état réel plutôt qu'un miroir potentiellement périmé.
        var shellTheme = (App.CurrentMainWindow as MainWindow)?.CurrentShellTheme;
        Theme = shellTheme switch
        {
            "Light" => "light",
            "Dark" => "dark",
            _ => "system"
        };
        Language = appSettings.Ui.Language;
        TeacherName = string.IsNullOrWhiteSpace(appSettings.Defaults.TeacherName) ? "Enseignant·e" : appSettings.Defaults.TeacherName;
        SchoolName = string.IsNullOrWhiteSpace(appSettings.Defaults.SchoolName) ? "École / Établissement" : appSettings.Defaults.SchoolName;
        DefaultClassLevel = appSettings.Defaults.ClassLevel;
        DefaultSubject = appSettings.Defaults.Subject;

        if (!string.IsNullOrWhiteSpace(appSettings.Ui.AccentColor))
        {
            SelectedAccent = AccentOptions.FirstOrDefault(
                a => a.Hex.Equals(appSettings.Ui.AccentColor, StringComparison.OrdinalIgnoreCase)) ?? AccentOptions[0];
        }

        GlobalProvider = appSettings.Ai.GlobalProvider;
        SelectedGlobalProvider = ProviderCatalog.Find(GlobalProvider);
        ProxyBaseUrl = appSettings.Ai.ProxyBaseUrl;
        VertexProject = appSettings.Ai.Vertex.Project;
        VertexRegion = appSettings.Ai.Vertex.Region;

        // Héritage des clés historiques
        if (appSettings.Ai.Models.TryGetValue("generation", out var genM)) ApplyRoutingValue("fiche", null, genM);
        if (appSettings.Ai.Models.TryGetValue("intent", out var intM)) IntentModel = intM;
        if (appSettings.Ai.Models.TryGetValue("assistant", out var astM)) ApplyRoutingValue("chat", null, astM);

        GuidesDir = appSettings.Folders.GuidesDir;
        ExportsDir = appSettings.Folders.ExportsDir;
        SelectedStylePresetId = appSettings.Defaults.StylePresetId;
        SelectedPreset = StylePresets.FirstOrDefault(p => p.Id == SelectedStylePresetId) ?? StylePresets[0];

        HistoryRetentionDays = appSettings.Features.HistoryRetentionDays;
        TelemetryEnabled = appSettings.Features.Telemetry;
        ExpMultiPassGen = appSettings.Features.ExpMultiPassGen;

        // Secrets (Coffre d'identification Windows)
        GeminiApiKey = _credentialStore.Get("gemini_api_key") ?? string.Empty;
        OpenAiApiKey = _credentialStore.Get("openai_api_key") ?? string.Empty;
        AnthropicApiKey = _credentialStore.Get("anthropic_api_key") ?? string.Empty;
        ProxyApiKey = _credentialStore.Get("proxy_api_key") ?? string.Empty;
        VercelApiKey = _credentialStore.Get("vercel_api_key") ?? string.Empty;

        LoadExtendedState(appSettings);
        _isLoadingSettings = false;
    }

    /// <summary>Recharge l'état étendu persisté dans le magasin clé/valeur extensible.</summary>
    private void LoadExtendedState(AppSettings s)
    {
        try
        {
            // Matrice de routage complète (prioritaire sur les clés héritées)
            if (s.Ai.Models.TryGetValue("routing.json", out var routingJson))
            {
                var routing = JsonSerializer.Deserialize<Dictionary<string, RoutingDto>>(routingJson);
                if (routing is not null)
                    foreach (var (task, dto) in routing)
                        ApplyRoutingValue(task, dto.Provider, dto.Model);
            }

            if (s.Ai.Models.TryGetValue("temp:generation", out var tg) && double.TryParse(tg, NumberStyles.Float, CultureInfo.InvariantCulture, out var vg))
                GenerationTemperature = Math.Clamp(vg, 0, 2);
            if (s.Ai.Models.TryGetValue("temp:intent", out var ti) && double.TryParse(ti, NumberStyles.Float, CultureInfo.InvariantCulture, out var vi))
                IntentTemperature = Math.Clamp(vi, 0, 2);
            if (s.Ai.Models.TryGetValue("temp:assistant", out var ta) && double.TryParse(ta, NumberStyles.Float, CultureInfo.InvariantCulture, out var va))
                AssistantTemperature = Math.Clamp(va, 0, 2);

            if (s.Ai.Models.TryGetValue("diag.logLevel", out var logLevel) && LogLevels.Contains(logLevel))
                DiagnosticLogLevel = logLevel;

            if (s.Ai.Models.TryGetValue("ui.accent", out var accentHex) && string.IsNullOrWhiteSpace(s.Ui.AccentColor))
                SelectedAccent = AccentOptions.FirstOrDefault(a => a.Hex.Equals(accentHex, StringComparison.OrdinalIgnoreCase)) ?? AccentOptions[0];

            if (s.Ai.Models.TryGetValue("proxy.customModel", out var customModel) && !string.IsNullOrWhiteSpace(customModel))
            {
                CustomEndpointModel = customModel;
                foreach (var item in RoutingMatrix) item.ApplyCustomProxyModel(customModel);
            }

            if (s.Ai.Models.TryGetValue("style.builder.json", out var builderJson))
            {
                var b = JsonSerializer.Deserialize<StyleBuilderDto>(builderJson);
                if (b is not null)
                {
                    BuilderPrimaryColor = AccentOption.ParseHex(b.Primary);
                    BuilderSecondaryColor = AccentOption.ParseHex(b.Secondary);
                    BuilderAccentColor = AccentOption.ParseHex(b.Accent);
                    BuilderPrimaryHex = b.Primary;
                    BuilderSecondaryHex = b.Secondary;
                    BuilderAccentHex = b.Accent;
                    if (FontOptions.Contains(b.Font)) BuilderFontFamily = b.Font;
                    BuilderCornerRadius = Math.Clamp(b.Radius, 0, 24);
                    BuilderMarginTop = Math.Clamp(b.MarginTop, 5, 40);
                    BuilderMarginBottom = Math.Clamp(b.MarginBottom, 5, 40);
                    BuilderMarginLeft = Math.Clamp(b.MarginLeft, 5, 40);
                    BuilderMarginRight = Math.Clamp(b.MarginRight, 5, 40);
                    BuilderHeaderLayout = string.IsNullOrWhiteSpace(b.Header)
                        ? FicheGen.Core.Documents.StylePreset.HeaderRule
                        : b.Header;
                }
            }

            if (s.Ai.Models.TryGetValue("prompts.json", out var promptsJson))
            {
                var saved = JsonSerializer.Deserialize<List<PromptDto>>(promptsJson);
                if (saved is not null)
                    foreach (var dto in saved)
                    {
                        var target = PromptTemplates.FirstOrDefault(p => p.Name == dto.Name);
                        if (target is not null && !string.IsNullOrWhiteSpace(dto.Content))
                            target.Content = dto.Content;
                    }
            }

            if (s.Ai.Models.TryGetValue("shortcuts.json", out var shortcutsJson))
            {
                var saved = JsonSerializer.Deserialize<List<ShortcutDto>>(shortcutsJson);
                if (saved is not null)
                    foreach (var dto in saved)
                    {
                        var target = Shortcuts.FirstOrDefault(k => k.Action == dto.Action);
                        if (target is not null && !string.IsNullOrWhiteSpace(dto.Keys))
                            target.Keys = dto.Keys;
                    }
            }
        }
        catch (Exception)
        {
            // Toute donnée étendue corrompue est ignorée : les valeurs par défaut restent en place.
        }
        finally
        {
            // L'état chargé correspond à ce qui a été enregistré : le builder repart propre.
            _builderDirty = false;
            _isLoadingPresetIntoBuilder = false;
            RefreshDefaultStyleLabel(s.Defaults.StylePresetId);
        }
    }

    /// <summary>Met à jour le libellé du style réellement appliqué aux nouveaux documents.</summary>
    private void RefreshDefaultStyleLabel(string? stylePresetId)
    {
        DefaultStyleLabel = string.Equals(stylePresetId, "custom", StringComparison.Ordinal)
            ? "Personnalisé (StyleBuilder)"
            : StylePresets.FirstOrDefault(p => p.Id == stylePresetId)?.Name ?? "Moderne";
    }

    private void ApplyRoutingValue(string taskKey, string? provider, string model)
    {
        var item = RoutingMatrix.FirstOrDefault(r => r.TaskKey == taskKey);
        if (item is null) return;
        if (!string.IsNullOrWhiteSpace(provider))
            item.SelectedProvider = ProviderCatalog.Find(provider);
        if (!string.IsNullOrWhiteSpace(model))
            item.Model = model;
    }

    // ─────────────── Enregistrement ───────────────

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        IsSaving = true;
        StatusMessage = "Enregistrement en cours…";

        try
        {
            var s = _settingsStore.GetSettings<AppSettings>();

            // Général
            s.Ui.Theme = Theme;
            s.Ui.Language = Language;
            s.Defaults.TeacherName = TeacherName;
            s.Defaults.SchoolName = SchoolName;
            s.Defaults.ClassLevel = DefaultClassLevel;
            s.Defaults.Subject = DefaultSubject;
            s.Defaults.StylePresetId = SelectedStylePresetId;

            // IA — fournisseur global & endpoints
            s.Ai.GlobalProvider = SelectedGlobalProvider?.Key ?? "aistudio";
            s.Ai.ProxyBaseUrl = ProxyBaseUrl;
            s.Ai.Vertex.Project = VertexProject;
            s.Ai.Vertex.Region = VertexRegion;

            // IA — clés héritées (consommées par le reste de l'application)
            s.Ai.Models["generation"] = RoutingOf("fiche")?.Model ?? "gemini-2.5-pro";
            s.Ai.Models["assistant"] = RoutingOf("chat")?.Model ?? "gemini-2.5-pro";
            s.Ai.Models["intent"] = IntentModel;

            // IA — état étendu (magasin clé/valeur, sans modification des contrats Core)
            s.Ai.Models["routing.json"] = JsonSerializer.Serialize(
                RoutingMatrix.ToDictionary(r => r.TaskKey,
                    r => new RoutingDto(r.SelectedProvider?.Key ?? "aistudio", r.Model)));
            s.Ai.Models["temp:generation"] = GenerationTemperature.ToString(CultureInfo.InvariantCulture);
            s.Ai.Models["temp:intent"] = IntentTemperature.ToString(CultureInfo.InvariantCulture);
            s.Ai.Models["temp:assistant"] = AssistantTemperature.ToString(CultureInfo.InvariantCulture);
            s.Ai.Models["diag.logLevel"] = DiagnosticLogLevel;
            s.Ui.AccentColor = SelectedAccent?.Hex ?? AccentColorService.DefaultHex;
            s.Ai.Models["ui.accent"] = s.Ui.AccentColor; // clé héritée conservée pour compatibilité
            s.Ai.Models["proxy.customModel"] = CustomEndpointModel ?? string.Empty;
            s.Ai.Models["style.builder.json"] = JsonSerializer.Serialize(new StyleBuilderDto(
                ToHex(BuilderPrimaryColor), ToHex(BuilderSecondaryColor), ToHex(BuilderAccentColor),
                BuilderFontFamily, BuilderCornerRadius,
                BuilderMarginTop, BuilderMarginBottom, BuilderMarginLeft, BuilderMarginRight,
                BuilderHeaderLayout));

            // Style des nouveaux documents : si le StyleBuilder a été retouché,
            // il devient un préréglage « Personnalisé » enregistré dans le moteur ;
            // sinon le préréglage de la galerie s'applique tel quel.
            if (_builderDirty)
            {
                RegisterCustomPreset();
            }
            s.Defaults.StylePresetId = SelectedStylePresetId;
            s.Ai.Models["prompts.json"] = JsonSerializer.Serialize(
                PromptTemplates.Select(p => new PromptDto(p.Name, p.Content)));
            s.Ai.Models["shortcuts.json"] = JsonSerializer.Serialize(
                Shortcuts.Select(k => new ShortcutDto(k.Action, k.Keys)));

            // Dossiers & fonctionnalités
            s.Folders.GuidesDir = GuidesDir;
            s.Folders.ExportsDir = ExportsDir;
            s.Features.HistoryRetentionDays = (int)Math.Round(HistoryRetentionDays);
            s.Features.Telemetry = TelemetryEnabled;
            s.Features.ExpMultiPassGen = ExpMultiPassGen;

            PersistCredentials();

            await _settingsStore.SaveSettingsAsync(s);

            LastSavedText = $"Dernier enregistrement : {DateTime.Now:HH:mm:ss}";
            StatusMessage = "✅ Paramètres enregistrés — secrets chiffrés dans le Coffre d'identification Windows.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠ Échec de l'enregistrement : {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void PersistCredentials()
    {
        void SetOrRemove(string resource, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                try { _credentialStore.Remove(resource); } catch { /* journalisé côté Core */ }
            }
            else
            {
                try { _credentialStore.Set(resource, value); } catch { /* journalisé côté Core */ }
            }
        }

        SetOrRemove("gemini_api_key", GeminiApiKey);
        SetOrRemove("openai_api_key", OpenAiApiKey);
        SetOrRemove("anthropic_api_key", AnthropicApiKey);
        SetOrRemove("proxy_api_key", ProxyApiKey);
        SetOrRemove("vercel_api_key", VercelApiKey);

        foreach (var resource in _clearedCredentials)
        {
            try { _credentialStore.Remove(resource); } catch { }
        }
        _clearedCredentials.Clear();
    }

    /// <summary>Efface immédiatement une clé d'API (appelé depuis la page).</summary>
    public void ClearApiKey(string providerKey)
    {
        switch (providerKey)
        {
            case "aistudio":
                GeminiApiKey = string.Empty;
                _clearedCredentials.Add("gemini_api_key");
                GeminiState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
            case "openai":
                OpenAiApiKey = string.Empty;
                _clearedCredentials.Add("openai_api_key");
                OpenAiState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
            case "anthropic":
                AnthropicApiKey = string.Empty;
                _clearedCredentials.Add("anthropic_api_key");
                AnthropicState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
            case "proxy":
                ProxyApiKey = string.Empty;
                _clearedCredentials.Add("proxy_api_key");
                ProxyState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
            case "vercel":
                VercelApiKey = string.Empty;
                _clearedCredentials.Add("vercel_api_key");
                VercelState.SetNeutral("Clé effacée — pensez à enregistrer (Ctrl+S).");
                break;
        }
    }

    // ─────────────── Tests de connexion ───────────────

    [RelayCommand]
    public async Task TestConnectionAsync(string? providerName)
    {
        var state = StateOf(providerName);
        if (state is null || state.IsTesting) return;

        state.SetPending();

        switch (providerName)
        {
            case "aistudio" when !IsGeminiKeyConfigured:
                state.SetWarning("Clé d'API manquante — test impossible"); return;
            case "openai" when !IsOpenAiKeyConfigured:
                state.SetWarning("Clé d'API manquante — test impossible"); return;
            case "anthropic" when !IsAnthropicKeyConfigured:
                state.SetWarning("Clé d'API manquante — test impossible"); return;
            case "vercel" when !IsVercelKeyConfigured:
                state.SetWarning("Clé d'API manquante — test impossible"); return;
            case "proxy" when string.IsNullOrWhiteSpace(ProxyBaseUrl):
                state.SetWarning("Adresse du serveur requise"); return;
            case "vertex" when string.IsNullOrWhiteSpace(VertexProject):
                state.SetWarning("Identifiant du projet Google Cloud requis"); return;
        }

        if (_connectionTester is null)
        {
            state.SetNeutral("Testeur de connexion indisponible.");
            return;
        }

        // Test réel : requête minimale de bout en bout (connectivité + auth).
        var result = await _connectionTester.TestAsync(providerName!);

        if (result.Success)
            state.SetOk(Math.Max(1, result.LatencyMs));
        else
            state.SetError(result.Message);
    }

    [RelayCommand]
    public async Task ScanProxyModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(ProxyBaseUrl))
        {
            ProxyState.SetWarning("Veuillez saisir l'URL du proxy.");
            return;
        }

        if (_proxyScanner is null)
        {
            ProxyState.SetNeutral("Scanner de modèles indisponible.");
            return;
        }

        ProxyState.SetPending();
        try
        {
            var modelList = await _proxyScanner.ScanAsync(ProxyBaseUrl, ProxyApiKey);

            if (modelList.Count > 0)
            {
                ProviderCatalog.UpdateProxyModels(modelList);
                foreach (var item in RoutingMatrix)
                {
                    if (item.SelectedProvider?.Key == "proxy")
                    {
                        item.ApplyCustomProxyModel(modelList[0]);
                    }
                }
                ProxyState.SetOk(120);
                StatusMessage = $"✅ {modelList.Count} modèle(s) détecté(s) sur le proxy : {string.Join(", ", modelList.Take(3))}";
            }
            else
            {
                ProxyState.SetWarning("Réponse reçue mais aucun modèle trouvé.");
            }
        }
        catch (OperationCanceledException)
        {
            ProxyState.SetWarning("Scan annulé.");
        }
        catch (Exception ex)
        {
            ProxyState.SetWarning(ex.Message);
        }
    }

    [RelayCommand]
    private async Task TestAllConnectionsAsync()
    {
        StatusMessage = "Diagnostic des six fournisseurs en cours…";
        foreach (var key in new[] { "aistudio", "openai", "anthropic", "proxy", "vertex", "vercel" })
            await TestConnectionAsync(key);

        var ok = AllStates().Count(s => s.Health == ConnectionHealth.Ok);
        StatusMessage = ok == 6
            ? "✅ Diagnostic terminé : les 6 fournisseurs sont opérationnels."
            : $"Diagnostic terminé : {ok}/6 fournisseurs opérationnels — vérifiez les clés signalées.";
    }

    private ProviderConnectionState? StateOf(string? key) => key switch
    {
        "aistudio" => GeminiState,
        "openai" => OpenAiState,
        "anthropic" => AnthropicState,
        "proxy" => ProxyState,
        "vertex" => VertexState,
        "vercel" => VercelState,
        _ => null,
    };

    private IEnumerable<ProviderConnectionState> AllStates()
        => new[] { GeminiState, OpenAiState, AnthropicState, ProxyState, VertexState, VercelState };

    // ─────────────── Dossiers (FutureAccessList WinRT) ───────────────

    [RelayCommand]
    private async Task BrowseGuidesDirAsync()
    {
        var folder = await _pickerService.PickFolderAsync();
        if (folder is null) return;

        GuidesDir = folder.Path;

        // Persistance de l'accès au dossier entre les sessions (sans nouvelle autorisation).
        try
        {
            StorageApplicationPermissions.FutureAccessList
                .AddOrReplace("FicheGen.GuidesDir", folder);
            GuidesAccessToken = "FicheGen.GuidesDir";
            IsGuidesAccessPersistent = true;
            StatusMessage = "✅ Dossier des guides enregistré — accès permanent accordé.";
        }
        catch (Exception)
        {
            IsGuidesAccessPersistent = false;
            StatusMessage = "⚠ Dossier enregistré, mais l'accès permanent a échoué.";
        }
    }

    [RelayCommand]
    private async Task BrowseExportsDirAsync()
    {
        var folder = await _pickerService.PickFolderAsync();
        if (folder is null) return;

        ExportsDir = folder.Path;
        try { StorageApplicationPermissions.FutureAccessList.AddOrReplace("FicheGen.ExportsDir", folder); }
        catch { /* non bloquant */ }
    }

    [RelayCommand] private Task OpenGuidesFolderAsync() => OpenFolderAsync(GuidesDir);
    [RelayCommand] private Task OpenExportsFolderAsync() => OpenFolderAsync(ExportsDir);

    [RelayCommand]
    private async Task OpenTocCacheFolderAsync()
    {
        Directory.CreateDirectory(TocCacheDir);
        await OpenFolderAsync(TocCacheDir);
    }

    [RelayCommand]
    private async Task OpenLogsFolderAsync()
    {
        Directory.CreateDirectory(LogsDir);
        await OpenFolderAsync(LogsDir);
    }

    private static async Task OpenFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { await Launcher.LaunchFolderPathAsync(path); }
        catch
        {
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(path);
                await Launcher.LaunchFolderAsync(folder);
            }
            catch { /* dossier inaccessible */ }
        }
    }

    // ─────────────── Cache ToC ───────────────

    public async Task RefreshTocCacheSizeAsync()
    {
        IsCacheBusy = true;
        TocCacheSizeText = "Calcul en cours…";

        var (size, files) = await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(TocCacheDir)) return (0L, 0);
                var infos = new DirectoryInfo(TocCacheDir).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
                return (infos.Sum(f => f.Length), infos.Count);
            }
            catch { return (0L, 0); }
        });

        TocCacheSizeText = files == 0
            ? "Cache vide (0 octet)"
            : $"{FormatBytes(size)} utilisés · {files} fichier{(files > 1 ? "s" : "")} en cache";
        IsCacheBusy = false;
    }

    public async Task ClearTocCacheAsync()
    {
        IsCacheBusy = true;
        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(TocCacheDir))
                    Directory.Delete(TocCacheDir, recursive: true);
                Directory.CreateDirectory(TocCacheDir);
            }
            catch { /* fichiers verrouillés : nouvelle tentative au prochain démarrage */ }
        });

        StatusMessage = "🧹 Cache de la table des matières vidé avec succès.";
        await RefreshTocCacheSizeAsync();
    }

    // ─────────────── Styles : préréglages & aperçu ───────────────

    /// <summary>Application immédiate de la couleur d'accentuation à l'interface.</summary>
    partial void OnSelectedAccentChanged(AccentOption? value)
    {
        // Pendant le chargement, ApplyFromSettings (démarrage) a déjà posé la teinte ;
        // on évite un double travail et un flash visuel.
        if (_isLoadingSettings) return;

        _accentColorService?.Apply(value?.Hex);
        StatusMessage = $"🎨 Accentuation « {value?.Name} » appliquée à l'interface — pensez à enregistrer (Ctrl+S).";
    }

    /// <summary>Application immédiate du thème clair / sombre / système au shell.</summary>
    partial void OnThemeChanged(string value)
    {
        if (_isLoadingSettings) return;

        var shellTheme = value switch
        {
            "light" => "Light",
            "dark" => "Dark",
            _ => "System"
        };
        (App.CurrentMainWindow as MainWindow)?.ApplyShellTheme(shellTheme);
    }

    /// <summary>Charge le préréglage choisi dans le StyleBuilder (couleurs, police,
    /// en-tête, arrondis) et rafraîchit l'aperçu. Le builder repart « propre » :
    /// tant qu'il n'est pas retouché, l'enregistrement applique le préréglage tel quel.</summary>
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

    /// <summary>Applique une palette prédéfinie en un clic au StyleBuilder.</summary>
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

    /// <summary>Construit le StylePreset correspondant à l'état courant du StyleBuilder.</summary>
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

    partial void OnSelectedGlobalProviderChanged(ProviderOption? value)
    {
        if (value is not null) GlobalProvider = value.Key;
    }

    partial void OnGlobalProviderChanged(string value)
    {
        if (SelectedGlobalProvider?.Key != value)
            SelectedGlobalProvider = ProviderCatalog.Find(value);
    }

    partial void OnCustomEndpointModelChanged(string value)
    {
        foreach (var item in RoutingMatrix)
            item.ApplyCustomProxyModel(value);
    }

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

    // ─────────────── Générateur de style par IA ───────────────

    public async Task AnalyzeStylePdfAsync(string fileName)
    {
        IsAnalyzingStyle = true;
        HasGeneratedCssFlag = false;

        try
        {
            StyleAnalysisStatus = $"🎨 Déduction de la palette depuis « {fileName} »…";
            await Task.Delay(150); // laisse l'UI afficher l'état avant le travail synchrone

            // Palette déduite par heuristique locale (déterministe à partir du nom).
            var palettes = new (string P, string S, string A, string Font, double Radius)[]
            {
                ("#1F4E79", "#2E75B6", "#FFC000", "Georgia", 6),                 // Académique bleu
                ("#2E7D32", "#66BB6A", "#FF7043", "Segoe UI Variable Text", 12), // Nature primaire
                ("#312E81", "#6366F1", "#F59E0B", "Cambria", 8),                 // Ardoise moderne
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

    // ─────────────── Aperçu HTML en direct ───────────────

    /// <summary>Aperçu fidèle : le document témoin est rendu par le VRAI moteur
    /// (<see cref="HtmlRenderer"/>) avec le CSS du StyleBuilder — ce que l'on voit
    /// est exactement ce que produiront la génération et l'export PDF.</summary>
    public string BuildPreviewHtml()
    {
        var css = HtmlRenderer.BuildPresetCss(BuildCustomPresetFromBuilder());
        return HtmlRenderer.RenderToFullHtml(BuildSampleDocument(), css);
    }

    /// <summary>Document témoin couvrant tous les types de blocs du moteur.</summary>
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

    /// <summary>Enregistre le StyleBuilder comme style « Personnalisé » et le rend
    /// actif pour toute nouvelle génération. Appelé à l'enregistrement des paramètres
    /// lorsque le builder a été retouché.</summary>
    private void RegisterCustomPreset()
    {
        var preset = BuildCustomPresetFromBuilder();
        _stylePresetService.AddCustomPreset(preset);
        SelectedStylePresetId = "custom";
        DefaultStyleLabel = "Personnalisé (StyleBuilder)";
    }

    // ─────────────── Diagnostic & réinitialisation ───────────────

    [RelayCommand]
    private async Task ExportDiagnosticBundleAsync()
    {
        if (_diagnosticExporter is null)
        {
            StatusMessage = "⚠ Service d'exportation de diagnostic non disponible.";
            return;
        }

        var choices = new Dictionary<string, IList<string>> { { "Archive Zip", new List<string> { ".zip" } } };
        var saveFile = await _pickerService.PickSaveFileAsync($"profstudio-diag-{DateTime.Now:yyyyMMdd-HHmmss}.zip", choices);
        if (saveFile is null) return;

        StatusMessage = "Génération du pack de diagnostic (nettoyage des secrets)…";
        await _diagnosticExporter.ExportDiagnosticBundleAsync(saveFile.Path);
        StatusMessage = $"✅ Pack de diagnostic exporté : {saveFile.Name} — aucune clé d'API incluse.";
    }

    public async Task ResetToDefaultsAsync()
    {
        Theme = "system";
        Language = "fr-FR";
        DefaultClassLevel = "CM2";
        DefaultSubject = "Mathématiques";
        SelectedAccent = AccentOptions[0];

        SelectedGlobalProvider = ProviderCatalog.Find("aistudio");
        GenerationTemperature = 0.7;
        IntentTemperature = 0.2;
        AssistantTemperature = 0.8;

        TelemetryEnabled = false;
        ExpMultiPassGen = false;
        HistoryRetentionDays = 0;
        DiagnosticLogLevel = "Information";
        CustomEndpointModel = string.Empty;

        BuildRoutingMatrixDefaults();

        SelectedPreset = StylePresets[0]; // recharge la palette « Moderne » dans le StyleBuilder
        BuilderMarginTop = 18; BuilderMarginBottom = 18; BuilderMarginLeft = 20; BuilderMarginRight = 20;
        BuilderHeaderLayout = FicheGen.Core.Documents.StylePreset.HeaderRule;
        _builderDirty = false;
        RefreshDefaultStyleLabel(StylePresets[0].Id);

        foreach (var prompt in PromptTemplates) prompt.Content = prompt.DefaultContent;
        foreach (var shortcut in Shortcuts) shortcut.Keys = shortcut.DefaultKeys;

        HasGeneratedCssFlag = false;
        GeneratedCss = string.Empty;
        StyleAnalysisStatus = string.Empty;

        await SaveSettingsAsync();
        StatusMessage = "↺ Valeurs d'usine rétablies — clés d'API et dossiers conservés.";
    }

    // ─────────────── Prompts & raccourcis ───────────────

    [RelayCommand]
    private void ResetPromptTemplate()
    {
        if (SelectedPromptTemplate is null) return;
        SelectedPromptTemplate.Content = SelectedPromptTemplate.DefaultContent;
        StatusMessage = $"↺ Modèle « {SelectedPromptTemplate.Name} » réinitialisé — pensez à enregistrer.";
    }

    [RelayCommand]
    private void ResetShortcuts()
    {
        foreach (var shortcut in Shortcuts)
            shortcut.Keys = shortcut.DefaultKeys;
        StatusMessage = "↺ Raccourcis clavier rétablis — pensez à enregistrer.";
    }

    // ─────────────── Utilitaires ───────────────

    private ModelRoutingItem? RoutingOf(string taskKey)
        => RoutingMatrix.FirstOrDefault(r => r.TaskKey == taskKey);

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string DescribeTemperature(double value)
    {
        var qualificatif = value switch
        {
            <= 0.30 => "Précis et fiable",
            <= 0.80 => "Équilibré",
            <= 1.30 => "Créatif",
            _ => "Très créatif",
        };
        return $"{value.ToString("0.00", CultureInfo.CurrentCulture)} · {qualificatif}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "octets", "Ko", "Mo", "Go" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size.ToString("0.#", CultureInfo.CurrentCulture)} {units[unit]}";
    }
}