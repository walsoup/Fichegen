using FicheGen.Core.Prompts;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Prompts;

public class PromptBuilderTests
{
    [Fact]
    public void BuildFichePrompt_IncludesClassLevelSubjectAndTopic()
    {
        var p = new FicheParameters("CM2", "Mathématiques", "Les fractions et décimaux", DurationMinutes: 45);

        var req = PromptBuilder.BuildFichePrompt(p);

        req.Purpose.Should().Be("fiche");
        req.ResponseJson.Should().BeTrue();
        req.UserPrompt.Should().Contain("CM2");
        req.UserPrompt.Should().Contain("Mathématiques");
        req.UserPrompt.Should().Contain("Les fractions et décimaux");
    }

    [Fact]
    public void BuildEvalPrompt_IncludesBaremeTarget()
    {
        var p = new EvalParameters("3e", "Histoire", "La Seconde Guerre Mondiale", TargetPoints: 20);

        var req = PromptBuilder.BuildEvalPrompt(p);

        req.Purpose.Should().Be("eval");
        req.UserPrompt.Should().Contain("3e");
        req.UserPrompt.Should().Contain("20 points");
    }
}
