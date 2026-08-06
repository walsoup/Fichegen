namespace FicheGen.Core.Storage;

public sealed record HistoryItem
{
    public required string Id { get; init; } = Guid.NewGuid().ToString();
    public required string Type { get; init; } = "fiche"; // "fiche" | "evaluation" | "quiz"
    public required string Title { get; init; }
    public string? ClassLevel { get; init; }
    public string? Subject { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public bool IsFavorite { get; init; }
    public required string PlainText { get; init; }
    public required string Html { get; init; }
    public string? SourceJson { get; init; }
    public string? StylePresetId { get; init; } = "modern";
}
