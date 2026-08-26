using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.App.Services;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Services;
using FicheGen.Core.Storage;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FicheGen.App.ViewModels;

// ═══════════════════════════════════════════════════════════════════
//  Types d'options et d'éléments (collections bindables XAML)
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
        SelectedProvider = ProviderCatalog.Find(providerKey);
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

internal static class ProviderCatalog
{
    public static readonly IReadOnlyList<ProviderOption> Providers = new List<ProviderOption>
    {
        new("aistudio", "Google AI Studio", "💠"),
        new("openai", "OpenAI", "🤖"),
        new("anthropic", "Anthropic Claude", "✒️"),
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

    public event EventHandler? PreviewRefreshRequested;

    private readonly HashSet<string> _clearedCredentials = new();
    private bool _isLoadingSettings;

    // ─────────────── État global ───────────────

    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string LastSavedText { get; set; } = "Aucune modification enregistrée pour le moment.";
    [ObservableProperty] public partial bool IsSaving { get; set; }

    private sealed record RoutingDto(string Provider, string Model);
    private sealed record StyleBuilderDto(string Primary, string Secondary, string Accent, string Font,
        double Radius, double MarginTop, double MarginBottom, double MarginLeft, double MarginRight,
        string? Header = null);
    private sealed record PromptDto(string Name, string Content);
    private sealed record ShortcutDto(string Action, string Keys);

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

    private void LoadSettings()
    {
        _isLoadingSettings = true;
        var appSettings = _settingsStore.GetSettings<AppSettings>();

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
        EnableStreaming = appSettings.Ui.EnableStreaming;

        GeminiApiKey = _credentialStore.Get("gemini_api_key") ?? string.Empty;
        OpenAiApiKey = _credentialStore.Get("openai_api_key") ?? string.Empty;
        AnthropicApiKey = _credentialStore.Get("anthropic_api_key") ?? string.Empty;
        ProxyApiKey = _credentialStore.Get("proxy_api_key") ?? string.Empty;
        VercelApiKey = _credentialStore.Get("vercel_api_key") ?? string.Empty;

        LoadExtendedState(appSettings);
        _isLoadingSettings = false;
    }

    private void LoadExtendedState(AppSettings s)
    {
        try
        {
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
        }
        finally
        {
            _builderDirty = false;
            _isLoadingPresetIntoBuilder = false;
            RefreshDefaultStyleLabel(s.Defaults.StylePresetId);
        }
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        IsSaving = true;
        StatusMessage = "Enregistrement en cours…";

        try
        {
            var s = _settingsStore.GetSettings<AppSettings>();

            s.Ui.Theme = Theme;
            s.Ui.Language = Language;
            s.Defaults.TeacherName = TeacherName;
            s.Defaults.SchoolName = SchoolName;
            s.Defaults.ClassLevel = DefaultClassLevel;
            s.Defaults.Subject = DefaultSubject;
            s.Defaults.StylePresetId = SelectedStylePresetId;

            s.Ai.GlobalProvider = SelectedGlobalProvider?.Key ?? "aistudio";
            s.Ai.ProxyBaseUrl = ProxyBaseUrl;
            s.Ai.Vertex.Project = VertexProject;
            s.Ai.Vertex.Region = VertexRegion;

            s.Ai.Models["generation"] = RoutingOf("fiche")?.Model ?? "gemini-2.5-pro";
            s.Ai.Models["assistant"] = RoutingOf("chat")?.Model ?? "gemini-2.5-pro";
            s.Ai.Models["intent"] = IntentModel;

            s.Ai.Models["routing.json"] = JsonSerializer.Serialize(
                RoutingMatrix.ToDictionary(r => r.TaskKey,
                    r => new RoutingDto(r.SelectedProvider?.Key ?? "aistudio", r.Model)));
            s.Ai.Models["temp:generation"] = GenerationTemperature.ToString(CultureInfo.InvariantCulture);
            s.Ai.Models["temp:intent"] = IntentTemperature.ToString(CultureInfo.InvariantCulture);
            s.Ai.Models["temp:assistant"] = AssistantTemperature.ToString(CultureInfo.InvariantCulture);
            s.Ai.Models["diag.logLevel"] = DiagnosticLogLevel;
            s.Ui.AccentColor = SelectedAccent?.Hex ?? AccentColorService.DefaultHex;
            s.Ai.Models["ui.accent"] = s.Ui.AccentColor;
            s.Ai.Models["proxy.customModel"] = CustomEndpointModel ?? string.Empty;
            s.Ai.Models["style.builder.json"] = JsonSerializer.Serialize(new StyleBuilderDto(
                ToHex(BuilderPrimaryColor), ToHex(BuilderSecondaryColor), ToHex(BuilderAccentColor),
                BuilderFontFamily, BuilderCornerRadius,
                BuilderMarginTop, BuilderMarginBottom, BuilderMarginLeft, BuilderMarginRight,
                BuilderHeaderLayout));

            if (_builderDirty)
            {
                RegisterCustomPreset();
            }
            s.Defaults.StylePresetId = SelectedStylePresetId;
            s.Ai.Models["prompts.json"] = JsonSerializer.Serialize(
                PromptTemplates.Select(p => new PromptDto(p.Name, p.Content)));
            s.Ai.Models["shortcuts.json"] = JsonSerializer.Serialize(
                Shortcuts.Select(k => new ShortcutDto(k.Action, k.Keys)));

            s.Folders.GuidesDir = GuidesDir;
            s.Folders.ExportsDir = ExportsDir;
            s.Features.HistoryRetentionDays = (int)Math.Round(HistoryRetentionDays);
            s.Features.Telemetry = TelemetryEnabled;
            s.Features.ExpMultiPassGen = ExpMultiPassGen;
            s.Ui.EnableStreaming = EnableStreaming;

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
                try { _credentialStore.Remove(resource); } catch { }
            }
            else
            {
                try { _credentialStore.Set(resource, value); } catch { }
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

        SelectedPreset = StylePresets[0];
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

    private ModelRoutingItem? RoutingOf(string taskKey)
        => RoutingMatrix.FirstOrDefault(r => r.TaskKey == taskKey);

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string FormatBytes(long bytes)
    {
        string[] units = { "octets", "Ko", "Mo", "Go" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size.ToString("0.#", CultureInfo.CurrentCulture)} {units[unit]}";
    }
}
