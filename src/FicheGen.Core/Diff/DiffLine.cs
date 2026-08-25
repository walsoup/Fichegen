namespace FicheGen.Core.Diff;

public enum DiffKind
{
    Unchanged,
    Added,
    Removed,
    Collapsed
}

public sealed record DiffLine(
    DiffKind Kind,
    string Text,
    int? OldLineNo = null,
    int? NewLineNo = null,
    int CollapsedCount = 0)
{
}

