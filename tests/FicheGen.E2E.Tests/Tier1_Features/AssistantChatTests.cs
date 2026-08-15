// ============================================================================
//  FicheGen.E2E.Tests — AssistantChatTests (Tier 1 Feature Coverage)
// ============================================================================

using System.Threading.Tasks;
using FicheGen.E2E.Tests.Infrastructure;
using FicheGen.E2E.Tests.Infrastructure.PageDrivers;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class AssistantChatTests : E2ETestBase
{
    [Fact]
    public void AssistantToggle_ClickButton_TogglesPaneVisibility()
    {
        // Arrange
        var mainDriver = new MainWindowDriver(Environment);
        mainDriver.IsAssistantVisible.Should().BeFalse();

        // Act
        mainDriver.ToggleAssistantVisibility(true);

        // Assert
        mainDriver.IsAssistantVisible.Should().BeTrue();
    }

    [Fact]
    public async Task AssistantChat_SendMessage_AppendsUserAndAssistantMessages()
    {
        // Arrange
        var driver = new AssistantDriver(Environment);

        // Act
        await driver.SendMessageAsync("Propose 3 exercices supplémentaires sur la géométrie.");

        // Assert
        driver.Messages.Should().HaveCount(2);
        driver.Messages[0].Sender.Should().Be("User");
        driver.Messages[0].Content.Should().Be("Propose 3 exercices supplémentaires sur la géométrie.");
        driver.Messages[1].Sender.Should().Be("Assistant");
        driver.Messages[1].Content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AssistantChat_RecallPreviousPrompt_NavigatesPromptHistory()
    {
        // Arrange
        var assistant = Environment.AssistantViewModel;
        assistant.PromptText = "Premier message";

        // Act
        assistant.RecallPreviousPrompt();

        // Assert
        assistant.PromptText.Should().Be("Premier message");
    }

    [Fact]
    public void AssistantChat_ApplySuggestion_FillsPromptInput()
    {
        // Arrange
        var assistant = Environment.AssistantViewModel;
        var suggestion = "Simplifie le vocabulaire pour des élèves en difficulté";

        // Act
        assistant.ApplySuggestion(suggestion);

        // Assert
        assistant.PromptText.Should().Be(suggestion);
        assistant.AreSuggestionsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task AssistantChat_ClearConversation_RemovesAllMessages()
    {
        // Arrange
        var driver = new AssistantDriver(Environment);
        await driver.SendMessageAsync("Message 1");
        driver.Messages.Should().NotBeEmpty();

        // Act
        driver.ClearConversation();

        // Assert
        driver.Messages.Should().BeEmpty();
        driver.StatusMessage.Should().Contain("Conversation effacée");
    }
}
