using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace FicheGen.Core.Diff;

public static class MyersDiffMapper
{
    public const int MaxLineThreshold = 20000;

    public static IReadOnlyList<DiffLine> ComputeDiff(string oldText, string newText)
    {
        if (oldText == null) oldText = string.Empty;
        if (newText == null) newText = string.Empty;

        var oldLinesCount = CountLines(oldText);
        var newLinesCount = CountLines(newText);

        if (oldLinesCount > MaxLineThreshold || newLinesCount > MaxLineThreshold)
        {
            return new List<DiffLine>
            {
                new(DiffKind.Collapsed, "Document volumineux — comparaison ligne à ligne désactivée", CollapsedCount: oldLinesCount)
            };
        }

        var diff = InlineDiffBuilder.Diff(oldText, newText);

        var rawLines = new List<DiffLine>();
        int oldNo = 1;
        int newNo = 1;

        foreach (var piece in diff.Lines)
        {
            switch (piece.Type)
            {
                case ChangeType.Unchanged:
                    rawLines.Add(new DiffLine(DiffKind.Unchanged, piece.Text ?? string.Empty, oldNo++, newNo++));
                    break;
                case ChangeType.Inserted:
                    rawLines.Add(new DiffLine(DiffKind.Added, piece.Text ?? string.Empty, OldLineNo: null, NewLineNo: newNo++));
                    break;
                case ChangeType.Deleted:
                    rawLines.Add(new DiffLine(DiffKind.Removed, piece.Text ?? string.Empty, OldLineNo: oldNo++, NewLineNo: null));
                    break;
                case ChangeType.Modified:
                    rawLines.Add(new DiffLine(DiffKind.Removed, piece.Text ?? string.Empty, OldLineNo: oldNo++, NewLineNo: null));
                    break;
            }
        }

        return CollapseUnchangedRuns(rawLines);
    }

    private static IReadOnlyList<DiffLine> CollapseUnchangedRuns(IReadOnlyList<DiffLine> lines)
    {
        var result = new List<DiffLine>();
        int i = 0;

        while (i < lines.Count)
        {
            if (lines[i].Kind == DiffKind.Unchanged)
            {
                int runStart = i;
                while (i < lines.Count && lines[i].Kind == DiffKind.Unchanged)
                {
                    i++;
                }

                int runLength = i - runStart;
                if (runLength >= 4)
                {
                    result.Add(lines[runStart]); // keep first
                    result.Add(new DiffLine(DiffKind.Collapsed, "...", CollapsedCount: runLength - 2));
                    result.Add(lines[i - 1]); // keep last
                }
                else
                {
                    for (int j = runStart; j < i; j++)
                    {
                        result.Add(lines[j]);
                    }
                }
            }
            else
            {
                result.Add(lines[i]);
                i++;
            }
        }

        return result;
    }

    private static int CountLines(string str)
    {
        if (string.IsNullOrEmpty(str)) return 0;
        int count = 1;
        for (int i = 0; i < str.Length; i++)
        {
            if (str[i] == '\n') count++;
        }
        return count;
    }
}
