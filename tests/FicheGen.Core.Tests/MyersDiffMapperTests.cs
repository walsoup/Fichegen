using FicheGen.Core.Diff;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests;

public class MyersDiffMapperTests
{
    [Fact]
    public void ComputeDiff_SimpleAdditionAndDeletion_ReturnsCorrectDiffLines()
    {
        var oldText = "Ligne 1\nLigne 2\nLigne 3";
        var newText = "Ligne 1\nLigne 2 modifiée\nLigne 3\nLigne 4 ajoutée";

        var lines = MyersDiffMapper.ComputeDiff(oldText, newText);

        lines.Should().NotBeEmpty();
        lines.Should().Contain(l => l.Kind == DiffKind.Added && l.Text.Contains("Ligne 4 ajoutée"));
        lines.Should().Contain(l => l.Kind == DiffKind.Removed && l.Text.Contains("Ligne 2"));
    }

    [Fact]
    public void ComputeDiff_LongUnchangedRun_CollapsesMiddleLines()
    {
        var lines1 = Enumerable.Range(1, 10).Select(i => $"Ligne {i}").ToList();
        var lines2 = new List<string>(lines1) { "Nouvelle ligne finale" };

        var oldText = string.Join("\n", lines1);
        var newText = string.Join("\n", lines2);

        var diff = MyersDiffMapper.ComputeDiff(oldText, newText);

        diff.Should().Contain(l => l.Kind == DiffKind.Collapsed && l.CollapsedCount > 0);
    }

    [Fact]
    public void ComputeDiff_ExceedingLineThreshold_ReturnsCollapsedWarningMarker()
    {
        var hugeText = new string('\n', MyersDiffMapper.MaxLineThreshold + 1);

        var diff = MyersDiffMapper.ComputeDiff(hugeText, "test");

        diff.Should().HaveCount(1);
        diff[0].Kind.Should().Be(DiffKind.Collapsed);
        diff[0].Text.Should().Contain("Document volumineux");
    }
}
