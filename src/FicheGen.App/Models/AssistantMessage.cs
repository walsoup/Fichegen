using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FicheGen.Core.Diff;

namespace FicheGen.App.Models;

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
    Collapsed
}

public partial class AssistantMessage : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUser))]
    public partial string Sender { get; set; } = "Assistant";

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    public partial System.DateTime Timestamp { get; set; } = System.DateTime.Now;

    [ObservableProperty]
    public partial bool IsStreaming { get; set; }

    [ObservableProperty]
    public partial bool IsApplied { get; set; }

    [ObservableProperty]
    public partial bool IsDiffResolved { get; set; }

    [ObservableProperty]
    public partial bool HasDiff { get; set; }

    [ObservableProperty]
    public partial IEnumerable<DiffLine>? DiffLines { get; set; }

    public bool IsUser => string.Equals(Sender, "User", System.StringComparison.Ordinal);

    public static AssistantMessage CreateAssistant(string content) =>
        new() { Sender = "Assistant", Content = content };

    public static AssistantMessage CreateUser(string content) =>
        new() { Sender = "User", Content = content };

    public void SetDiff(IEnumerable<FicheGen.Core.Diff.DiffLine>? diffLines)
    {
        DiffLines = diffLines;
        HasDiff = diffLines != null && diffLines.Any();
    }

    public string GetModifiedText()
    {
        if (DiffLines == null) return Content;
        var sb = new System.Text.StringBuilder();
        foreach (var line in DiffLines)
        {
            if (line.Kind == FicheGen.Core.Diff.DiffKind.Unchanged || line.Kind == FicheGen.Core.Diff.DiffKind.Added)
            {
                sb.AppendLine(line.Text);
            }
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }
}
