// ============================================================================
//  FicheGen.E2E.Tests — GenerationCancellationTests (Tier 1 Feature Coverage)
// ============================================================================

using System;
using System.Threading.Tasks;
using FicheGen.App.ViewModels;
using FicheGen.Core.Documents;
using FicheGen.E2E.Tests.Infrastructure;
using FicheGen.E2E.Tests.Infrastructure.PageDrivers;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class GenerationCancellationTests : E2ETestBase
{
    [Fact]
    public async Task Cancel_DuringActiveGeneration_StopsExecutionAndUpdatesStatus()
    {
        // Arrange
        Environment.LlmClient.SimulateDelay = true;
        Environment.LlmClient.DelayMs = 2000;
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "CM2", subject: "Mathématiques", topic: "Les nombres décimaux", durationMinutes: 60);

        // Act
        var genTask = driver.TriggerGenerationAsync();
        await Task.Delay(100);
        driver.CancelGeneration();
        await genTask;

        // Assert
        driver.IsGenerating.Should().BeFalse();
        driver.StatusSeverity.Should().Be(StatusSeverity.Warning);
        driver.StatusMessage.Should().Contain("annulée");
    }

    [Fact]
    public void Cancel_WhenIdle_CommandCanExecuteIsFalse()
    {
        // Arrange & Act
        var canCancel = Environment.FicheFormViewModel.CancelGenerationCommand.CanExecute(null);

        // Assert
        canCancel.Should().BeFalse();
    }

    [Fact]
    public async Task Cancel_ResetsIsGeneratingFlag_AllowsSubsequentGeneration()
    {
        // Arrange
        Environment.LlmClient.SimulateDelay = true;
        Environment.LlmClient.DelayMs = 1500;
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "CM1", subject: "Histoire", topic: "Les Gaulois", durationMinutes: 45);

        // Act
        var task1 = driver.TriggerGenerationAsync();
        await Task.Delay(50);
        driver.CancelGeneration();
        await task1;

        Environment.LlmClient.SimulateDelay = false;
        await driver.TriggerGenerationAsync();

        // Assert
        driver.IsGenerating.Should().BeFalse();
        driver.CurrentDocument.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancel_DuringAssistantStreaming_HaltsResponse()
    {
        // Arrange
        Environment.LlmClient.SimulateDelay = true;
        Environment.LlmClient.DelayMs = 200;
        var doc = new GeneratedDocument(new DocumentMetadata("Fiche Volcans", "", "CM2", "Sciences", 60, "#000", "modern"), [new ParagraphBlock("Contenu sur les volcans.")]);
        Environment.ResultViewModel.LoadDocument(doc, "<p>Contenu sur les volcans.</p>");
        var assistant = new AssistantDriver(Environment);

        // Act
        var msgTask = assistant.SendMessageAsync("Explique les volcans.");
        await Task.Delay(50);
        Environment.AssistantViewModel.CancelStreaming();
        await msgTask;

        // Assert
        assistant.IsStreaming.Should().BeFalse();
        assistant.StatusMessage.Should().Contain("annulé");
    }

    [Fact]
    public async Task Cancel_MultipleConsecutiveGenerations_CancelsPreviousTask()
    {
        // Arrange
        Environment.LlmClient.SimulateDelay = true;
        Environment.LlmClient.DelayMs = 1000;
        var driver = new FicheFormDriver(Environment);
        driver.FillFicheForm(classLevel: "CE2", subject: "Sciences", topic: "Le système solaire", durationMinutes: 60);

        // Act
        var firstTask = driver.TriggerGenerationAsync();
        await Task.Delay(50);
        
        Environment.LlmClient.SimulateDelay = false;
        var secondTask = driver.TriggerGenerationAsync();

        await Task.WhenAll(firstTask, secondTask);

        // Assert
        driver.IsGenerating.Should().BeFalse();
        driver.CurrentDocument.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancel_EvaluationAndQuizGeneration_ResetsIsGenerating()
    {
        // Arrange
        Environment.LlmClient.SimulateDelay = true;
        Environment.LlmClient.DelayMs = 2000;

        Environment.EvaluationViewModel.ClassLevel = "CM1";
        Environment.EvaluationViewModel.Subject = "Histoire";
        Environment.EvaluationViewModel.Topics = "Le Moyen Âge";

        // Act & Assert for Evaluation
        var evalTask = Environment.EvaluationViewModel.GenerateEvaluationAsync();
        await Task.Delay(50);
        Environment.EvaluationViewModel.CancelGeneration();
        await evalTask;
        Environment.EvaluationViewModel.IsGenerating.Should().BeFalse();

        // Arrange for Quiz
        Environment.QuizViewModel.ClassLevel = "CM1";
        Environment.QuizViewModel.Subject = "Histoire";
        Environment.QuizViewModel.Topic = "Le Moyen Âge";

        // Act & Assert for Quiz
        var quizTask = Environment.QuizViewModel.GenerateQuizAsync();
        await Task.Delay(50);
        Environment.QuizViewModel.CancelGeneration();
        await quizTask;
        Environment.QuizViewModel.IsGenerating.Should().BeFalse();
    }
}
