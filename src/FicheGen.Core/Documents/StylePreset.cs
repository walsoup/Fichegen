namespace FicheGen.Core.Documents;

public sealed record StylePreset
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string PrimaryColor { get; init; } = "#2563EB";
    public required string SecondaryColor { get; init; } = "#1E40AF";
    public required string FontFamily { get; init; } = "Segoe UI, sans-serif";
    public int MarginMm { get; init; } = 20;
    public string? CustomCss { get; init; }

    public static StylePreset Modern => new()
    {
        Id = "modern",
        Name = "Moderne",
        PrimaryColor = "#2563EB",
        SecondaryColor = "#1E40AF",
        FontFamily = "'Segoe UI', system-ui, -apple-system, sans-serif",
        MarginMm = 20
    };
}
