namespace FicheGen.Core.Toc;

public sealed record ParentDocumentItem(
    string FilePath,
    string FileName,
    string DropdownLabel,
    string DocumentType,
    string Level,
    string Extra,
    IReadOnlyList<ToCEntry> Lessons)
{
    public string LessonsSummary => Lessons.Count > 0 ? $"{Lessons.Count} leçons" : "Non extrait";
    public bool IsIndexed => Lessons.Count > 0;
}

public sealed record ParentDocumentsIndex(
    DateTime LastScanUtc,
    string ParentDirectory,
    IReadOnlyList<ParentDocumentItem> Documents);