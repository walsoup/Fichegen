namespace FicheGen.Core.Storage;

public sealed class UiSettings
{
    public string Theme { get; set; } = "system";
    public string Language { get; set; } = "fr-FR";
    public string AccentColor { get; set; } = string.Empty;
    public bool AssistantPaneOpen { get; set; } = true;
    public double SplitRatio { get; set; } = 0.38;
    public bool EnableStreaming { get; set; } = true;
}

public sealed class DefaultSettings
{
    public string TeacherName { get; set; } = "Enseignant·e";
    public string SchoolName { get; set; } = "École / Établissement";
    public string ClassLevel { get; set; } = "CM2";
    public string Subject { get; set; } = "Mathématiques";
    public string StylePresetId { get; set; } = "modern";
}

public sealed class ProviderOverride
{
    public string Provider { get; set; } = "aistudio";
    public string? Model { get; set; }
}

public sealed class VertexSettings
{
    public string Project { get; set; } = string.Empty;
    public string Region { get; set; } = "europe-west1";
}

public sealed class TemperatureSettings
{
    public double Generation { get; set; } = 0.7;
    public double Intent { get; set; } = 0.1;
}

public sealed class AiSettings
{
    public string GlobalProvider { get; set; } = "cloud";
    public Dictionary<string, string> Models { get; set; } = new();
    public Dictionary<string, ProviderOverride> RoutingOverrides { get; set; } = new();
    public string ProxyBaseUrl { get; set; } = "http://localhost:11434/v1";
    public VertexSettings Vertex { get; set; } = new();
    public TemperatureSettings Temperatures { get; set; } = new();

    // Transient key storage for UI binding; extracted & saved to CredentialStore, stripped from disk JSON
    [System.Text.Json.Serialization.JsonIgnore]
    public string GeminiApiKey { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string OpenAiApiKey { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string AnthropicApiKey { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string ProxyApiKey { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string VercelApiKey { get; set; } = string.Empty;
}

public sealed class FolderSettings
{
    public string GuidesDir { get; set; } = string.Empty;
    public string ExportsDir { get; set; } = string.Empty;
}

public sealed class FeatureSettings
{
    public bool ExpMultiPassGen { get; set; } = false;
    public bool Telemetry { get; set; } = false;
    public int HistoryRetentionDays { get; set; } = 0;
    public bool EnableExpertMode { get; set; } = false;
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 3;
    public bool IsFirstRunCompleted { get; set; } = false;
    public UiSettings Ui { get; set; } = new();
    public DefaultSettings Defaults { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public FolderSettings Folders { get; set; } = new();
    public FeatureSettings Features { get; set; } = new();
}
