// ============================================================================
//  FicheGen.E2E.Tests — AssistantDriver
//  Pilote pour le volet de l'assistant pédagogique conversationnel.
// ============================================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using FicheGen.App.ViewModels;

namespace FicheGen.E2E.Tests.Infrastructure.PageDrivers;

public sealed class AssistantDriver
{
    private readonly TestEnvironment _env;

    public AssistantDriver(TestEnvironment env)
    {
        _env = env;
    }

    public void SetPromptText(string prompt)
    {
        _env.AssistantViewModel.PromptText = prompt;
    }

    public async Task SendMessageAsync(string text)
    {
        _env.AssistantViewModel.PromptText = text;
        await _env.AssistantViewModel.SendMessageAsync();
    }

    public void SelectMode(string mode)
    {
        _env.AssistantViewModel.SelectedMode = mode;
    }

    public void ClearConversation()
    {
        _env.AssistantViewModel.ClearConversation();
    }

    public IReadOnlyList<ChatMessageItem> Messages => _env.AssistantViewModel.Messages;
    public bool IsStreaming => _env.AssistantViewModel.IsStreaming;
    public string StatusMessage => _env.AssistantViewModel.StatusMessage;
}
