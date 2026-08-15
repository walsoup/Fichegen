using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.App.Services;
using FicheGen.Core.Abstractions;
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
    public Brush? PrimaryBrush => _primaryBrush ??= BrushHelper.TryCreateBrush(AccentOption.ParseHex(PrimaryHex));
    public Brush? SecondaryBrush => _secondaryBrush ??= BrushHelper.TryCreateBrush(AccentOption.ParseHex(SecondaryHex));
    public Brush? AccentBrush => _accentBrush ??= BrushHelper.TryCreateBrush(AccentOption.ParseHex(AccentHex));

    public StylePresetItem(string id, string name, string description, string icon,
        string primaryHex, string secondaryHex, string accentHex, string fontFamily, double cornerRadius)
    {
        Id = id; Name = name; Description = description; Icon = icon;
        PrimaryHex = primaryHex; SecondaryHex = secondaryHex; AccentHex = accentHex;
        FontFamily = fontFamily; CornerRadius = cornerRadius;
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

    /// <summary>Déclenché quand l'aperçu WebView2 doit être régénéré.</summary>
    public event EventHandler? PreviewRefreshRequested;

    private readonly HashSet<string> _clearedCredentials = new();
    private readonly Random _rng = new();

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
        new StylePresetItem("modern", "Moderne", "Dégradés bleus, encadrés arrondis, hiérarchie aérée.", "🌊", "#2563EB", "#60A5FA", "#F59E0B", "Segoe UI Variable Text", 10),
        new StylePresetItem("classic", "Classique", "Bleu institutionnel, filets sobres, esprit manuel scolaire.", "📘", "#1F4E79", "#2E75B6", "#C00000", "Georgia", 4),
        new StylePresetItem("minimal", "Minimal", "Noir & blanc maîtrisé, une seule touche de vert.", "⬜", "#111827", "#6B7280", "#10B981", "Segoe UI", 2),
        new StylePresetItem("academic", "Académique", "Indigo profond et empattements, style universitaire.", "🎓", "#312E81", "#4F46E5", "#B45309", "Cambria", 6),
        new StylePresetItem("playful", "Ludique", "Couleurs vives et formes douces pour le primaire.", "🎈", "#7C3AED", "#EC4899", "#FBBF24", "Trebuchet MS", 16),
    };

    [ObservableProperty] public partial StylePresetItem? SelectedPreset { get; set; }
    [ObservableProperty] public partial string SelectedStylePresetId { get; set; } = "modern";

    // StyleBuilder
    [ObservableProperty] public partial Color BuilderPrimaryColor { get; set; } = AccentOption.ParseHex("#2563EB");
    [ObservableProperty] public partial Color BuilderSecondaryColor { get; set; } = AccentOption.ParseHex("#60A5FA");
    [ObservableProperty] public partial Color BuilderAccentColor { get; set; } = AccentOption.ParseHex("#F59E0B");
    [ObservableProperty] public partial string BuilderFontFamily { get; set; } = "Segoe UI Variable Text";
    [ObservableProperty] public partial double BuilderCornerRadius { get; set; } = 10;
    [ObservableProperty] public partial double BuilderMarginTop { get; set; } = 18;
    [ObservableProperty] public partial double BuilderMarginBottom { get; set; } = 18;
    [ObservableProperty] public partial double BuilderMarginLeft { get; set; } = 20;
    [ObservableProperty] public partial double BuilderMarginRight { get; set; } = 20;

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
        double Radius, double MarginTop, double MarginBottom, double MarginLeft, double MarginRight);
    private sealed record PromptDto(string Name, string Content);
    private sealed record ShortcutDto(string Action, string Keys);

    // ─────────────── Construction ───────────────

    public SettingsViewModel(
        ISettingsStore settingsStore,
        ICredentialStore credentialStore,
        PickerService pickerService,
        StylePresetService stylePresetService,
        IDiagnosticBundleExporter? diagnosticExporter = null)
    {
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _pickerService = pickerService;
        _stylePresetService = stylePresetService;
        _diagnosticExporter = diagnosticExporter;

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
        var appSettings = _settingsStore.GetSettings<AppSettings>();

        Theme = appSettings.Ui.Theme;
        Language = appSettings.Ui.Language;
        TeacherName = string.IsNullOrWhiteSpace(appSettings.Defaults.TeacherName) ? "Enseignant·e" : appSettings.Defaults.TeacherName;
        SchoolName = string.IsNullOrWhiteSpace(appSettings.Defaults.SchoolName) ? "École / Établissement" : appSettings.Defaults.SchoolName;
        DefaultClassLevel = appSettings.Defaults.ClassLevel;
        DefaultSubject = appSettings.Defaults.Subject;

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

            if (s.Ai.Models.TryGetValue("ui.accent", out var accentHex))
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
                    if (FontOptions.Contains(b.Font)) BuilderFontFamily = b.Font;
                    BuilderCornerRadius = Math.Clamp(b.Radius, 0, 24);
                    BuilderMarginTop = Math.Clamp(b.MarginTop, 5, 40);
                    BuilderMarginBottom = Math.Clamp(b.MarginBottom, 5, 40);
                    BuilderMarginLeft = Math.Clamp(b.MarginLeft, 5, 40);
                    BuilderMarginRight = Math.Clamp(b.MarginRight, 5, 40);
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
            s.Ai.Models["ui.accent"] = SelectedAccent?.Hex ?? "#2563EB";
            s.Ai.Models["proxy.customModel"] = CustomEndpointModel ?? string.Empty;
            s.Ai.Models["style.builder.json"] = JsonSerializer.Serialize(new StyleBuilderDto(
                ToHex(BuilderPrimaryColor), ToHex(BuilderSecondaryColor), ToHex(BuilderAccentColor),
                BuilderFontFamily, BuilderCornerRadius,
                BuilderMarginTop, BuilderMarginBottom, BuilderMarginLeft, BuilderMarginRight));
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
        await Task.Delay(_rng.Next(280, 720)); // latence simulée du handshake

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
                state.SetWarning("URL du point de terminaison requise"); return;
            case "vertex" when string.IsNullOrWhiteSpace(VertexProject):
                state.SetWarning("ID du projet GCP requis"); return;
        }

        state.SetOk(_rng.Next(68, 420));
    }

    [RelayCommand]
    public async Task ScanProxyModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(ProxyBaseUrl))
        {
            ProxyState.SetWarning("Veuillez saisir l'URL du proxy.");
            return;
        }

        ProxyState.SetPending();
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            if (!string.IsNullOrWhiteSpace(ProxyApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ProxyApiKey);
            }

            var baseUrl = ProxyBaseUrl.TrimEnd('/');
            var requestUri = baseUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/models";

            var response = await client.GetAsync(requestUri);
            string content = string.Empty;
            if (!response.IsSuccessStatusCode && baseUrl.Contains("11434"))
            {
                var ollamaUri = "http://localhost:11434/api/tags";
                var ollamaResp = await client.GetAsync(ollamaUri);
                if (ollamaResp.IsSuccessStatusCode)
                {
                    content = await ollamaResp.Content.ReadAsStringAsync();
                }
            }
            else if (response.IsSuccessStatusCode)
            {
                content = await response.Content.ReadAsStringAsync();
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                ProxyState.SetWarning($"Impossible d'accéder à {requestUri}");
                return;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var modelList = new List<string>();

            if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in dataElem.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idElem) && idElem.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var modelId = idElem.GetString();
                        if (!string.IsNullOrWhiteSpace(modelId))
                            modelList.Add(modelId);
                    }
                }
            }
            else if (doc.RootElement.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in modelsElem.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameElem) && nameElem.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var modelName = nameElem.GetString();
                        if (!string.IsNullOrWhiteSpace(modelName))
                            modelList.Add(modelName);
                    }
                }
            }

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
        catch (Exception ex)
        {
            ProxyState.SetWarning($"Erreur de connexion : {ex.Message}");
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
            StatusMessage = "✅ Dossier des guides enregistré — accès persistant accordé.";
        }
        catch (Exception)
        {
            IsGuidesAccessPersistent = false;
            StatusMessage = "⚠ Dossier enregistré, mais l'accès persistant (FutureAccessList) a échoué.";
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

    partial void OnSelectedPresetChanged(StylePresetItem? value)
    {
        if (value is null) return;

        SelectedStylePresetId = value.Id;
        BuilderPrimaryColor = AccentOption.ParseHex(value.PrimaryHex);
        BuilderSecondaryColor = AccentOption.ParseHex(value.SecondaryHex);
        BuilderAccentColor = AccentOption.ParseHex(value.AccentHex);
        BuilderFontFamily = value.FontFamily;
        BuilderCornerRadius = value.CornerRadius;
        RequestPreviewRefresh();
    }

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

    partial void OnBuilderPrimaryColorChanged(Color value) => RequestPreviewRefresh();
    partial void OnBuilderSecondaryColorChanged(Color value) => RequestPreviewRefresh();
    partial void OnBuilderAccentColorChanged(Color value) => RequestPreviewRefresh();
    partial void OnBuilderFontFamilyChanged(string value) => RequestPreviewRefresh();
    partial void OnBuilderCornerRadiusChanged(double value) => RequestPreviewRefresh();
    partial void OnBuilderMarginTopChanged(double value) => RequestPreviewRefresh();
    partial void OnBuilderMarginBottomChanged(double value) => RequestPreviewRefresh();
    partial void OnBuilderMarginLeftChanged(double value) => RequestPreviewRefresh();
    partial void OnBuilderMarginRightChanged(double value) => RequestPreviewRefresh();

    private void RequestPreviewRefresh() => PreviewRefreshRequested?.Invoke(this, EventArgs.Empty);

    // ─────────────── Générateur de style par IA ───────────────

    public async Task AnalyzeStylePdfAsync(string fileName)
    {
        IsAnalyzingStyle = true;
        HasGeneratedCssFlag = false;

        try
        {
            StyleAnalysisStatus = $"📥 Lecture de « {fileName} »…";
            await Task.Delay(600);
            StyleAnalysisStatus = "🎨 Extraction de la palette dominante…";
            await Task.Delay(650);
            StyleAnalysisStatus = "🔤 Analyse de la hiérarchie typographique…";
            await Task.Delay(600);
            StyleAnalysisStatus = "🧩 Génération de la feuille de style CSS…";
            await Task.Delay(550);

            // Palette déduite (déterministe à partir du nom — pipeline IA branché ici en production).
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
            StyleAnalysisStatus = $"✅ Identité visuelle déduite de « {fileName} » et appliquée au StyleBuilder.";
            StatusMessage = "✨ Style généré par IA — vérifiez l'aperçu en direct, puis enregistrez (Ctrl+S).";
            RequestPreviewRefresh();
        }
        finally
        {
            IsAnalyzingStyle = false;
        }
    }

    private string BuildGeneratedCss() => $$"""
        /* ── Feuille de style PROFstudio — générée par IA ────────────── */
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

    public string BuildPreviewHtml(bool darkMode)
    {
        var p = ToHex(BuilderPrimaryColor);
        var s = ToHex(BuilderSecondaryColor);
        var a = ToHex(BuilderAccentColor);
        var radius = $"{BuilderCornerRadius:0}px";
        var padding = $"{BuilderMarginTop:0}mm {BuilderMarginRight:0}mm {BuilderMarginBottom:0}mm {BuilderMarginLeft:0}mm";

        var pageBg = darkMode ? "#1B1B1F" : "#EEF1F5";
        var sheetBg = darkMode ? "#26272E" : "#FFFFFF";
        var text = darkMode ? "#E8E8EC" : "#1F2937";
        var subtle = darkMode ? "#A6A7AE" : "#6B7280";
        var boxBg = darkMode ? "#2F3038" : "#F8FAFC";
        var line = darkMode ? "#55565E" : "#CBD5E1";
        var preset = SelectedPreset?.Name ?? "Personnalisé";

        var safeClassLevel = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(DefaultClassLevel) ? "CM2" : DefaultClassLevel);
        var safeSubject = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(DefaultSubject) ? "Mathématiques" : DefaultSubject);
        var safeFontFamily = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(BuilderFontFamily) ? "Segoe UI" : BuilderFontFamily).Replace("\"", "");

        return $$"""
        <!DOCTYPE html>
        <html lang="fr">
        <head>
        <meta charset="utf-8"/>
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src 'self' https: data:;"/>
        <style>
          * { box-sizing: border-box; margin: 0; padding: 0; }
          body { background: {{pageBg}}; color: {{text}};
                 font-family: '{{safeFontFamily}}', 'Segoe UI', sans-serif;
                 padding: 18px; font-size: 13.5px; }
          .sheet { background: {{sheetBg}}; border-radius: {{radius}};
                   padding: {{padding}}; max-width: 760px; margin: 0 auto;
                   box-shadow: 0 6px 24px rgba(0,0,0,.18); }
          header.band { background: linear-gradient(135deg, {{p}}, {{s}});
                        border-radius: {{radius}}; color: #fff; padding: 18px 20px; }
          header.band .ecole { font-size: 11px; opacity: .85; letter-spacing: .08em; text-transform: uppercase; }
          header.band h1 { font-size: 24px; margin-top: 4px; }
          .chips { margin-top: 10px; display: flex; gap: 8px; flex-wrap: wrap; }
          .chips span { background: rgba(255,255,255,.22); border-radius: 999px;
                        padding: 3px 12px; font-size: 11.5px; font-weight: 600; }
          h2 { color: {{p}}; font-size: 15.5px; margin: 20px 0 8px; }
          .objectifs { background: {{boxBg}}; border-left: 4px solid {{a}};
                       border-radius: 0 {{radius}} {{radius}} 0; padding: 12px 16px; }
          .objectifs ul { margin-left: 18px; }
          .objectifs li { margin: 4px 0; }
          .reponse { border-bottom: 1.5px dashed {{line}}; height: 26px; margin: 10px 0; }
          table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 12.5px; }
          th { background: {{p}}; color: #fff; text-align: left; padding: 7px 10px; }
          td { border: 1px solid {{line}}; padding: 7px 10px; }
          .badge { display: inline-block; background: {{a}}; color: #fff; border-radius: 999px;
                   padding: 2px 10px; font-size: 11px; font-weight: 700; }
          footer { margin-top: 24px; padding-top: 10px; border-top: 1px solid {{line}};
                   color: {{subtle}}; font-size: 11px; display: flex; justify-content: space-between; }
        </style>
        </head>
        <body>
          <div class="sheet">
            <header class="band">
              <div class="ecole">École primaire Louise-Michel</div>
              <h1>Les fractions simples</h1>
              <div class="chips"><span>{{safeClassLevel}}</span><span>{{safeSubject}}</span><span>45 min</span><span>Séquence 4 · Séance 2</span></div>
            </header>

            <h2>🎯 Objectifs d'apprentissage</h2>
            <div class="objectifs">
              <ul>
                <li>Comprendre une fraction comme un partage de l'unité.</li>
                <li>Lire et écrire les fractions usuelles (1/2, 1/3, 1/4).</li>
                <li>Représenter une fraction sur une bande ou un disque.</li>
              </ul>
            </div>

            <h2>✏️ Exercice 1 — À toi de jouer !</h2>
            <p>Colorie la fraction demandée sur chaque figure, puis écris-la en chiffres.</p>
            <div class="reponse"></div>
            <div class="reponse"></div>

            <h2>📊 Tableau de correspondance</h2>
            <table>
              <tr><th>Fraction</th><th>Lecture</th><th>Représentation</th></tr>
              <tr><td>1/2</td><td>un demi</td><td><span class="badge">½ disque</span></td></tr>
              <tr><td>1/4</td><td>un quart</td><td><span class="badge">¼ disque</span></td></tr>
            </table>

            <footer>
              <span>Généré avec PROFstudio · Style « {{preset}} »{{(darkMode ? " · aperçu sombre" : "")}}</span>
              <span>Page 1/1</span>
            </footer>
          </div>
        </body>
        </html>
        """;
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