using System;
using System.Collections.Specialized;
using System.ComponentModel;
using FicheGen.App.Models;
using FicheGen.App.ViewModels;
using FicheGen.App.Views.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace FicheGen.App.Views;

/// <summary>
/// Interface implémentée par les pages disposant d'un volet Assistant IA latéral.
/// </summary>
public interface IAssistantHostPage
{
    void SetAssistantVisible(bool isVisible);
}

public static class AssistantHostHelper
{
    public static void WireAssistantPane(AssistantPane pane)
    {
        var assistantVm = App.Services.GetRequiredService<AssistantViewModel>();
        var resultVm = App.Services.GetRequiredService<ResultViewModel>();

        pane.DataContext = assistantVm;

        EventHandler<AssistantRequestEventArgs> sendHandler = async (s, e) =>
        {
            assistantVm.SelectedMode = e.Mode;
            assistantVm.PromptText = e.Prompt;
            await assistantVm.SendMessageAsync();
        };

        EventHandler stopHandler = (s, e) => assistantVm.CancelStreaming();

        EventHandler newConvHandler = (s, e) =>
        {
            assistantVm.ClearConversation();
            pane.Messages.Clear();
        };

        EventHandler<AssistantDiffEventArgs> applyDiffHandler = (s, e) =>
        {
            if (e.Message != null)
            {
                var modifiedText = e.Message.GetModifiedText();
                resultVm.PushSnapshot("Modification appliquée via Assistant IA");
                resultVm.CurrentHtml = modifiedText;
                e.Message.IsApplied = true;
            }
        };

        PropertyChangedEventHandler propHandler = (s, e) =>
        {
            if (e.PropertyName == nameof(AssistantViewModel.IsStreaming))
            {
                pane.IsStreaming = assistantVm.IsStreaming;
            }
        };

        void SyncMessages()
        {
            pane.Messages.Clear();
            foreach (var m in assistantVm.Messages)
            {
                var msg = m.IsUser ? AssistantMessage.CreateUser(m.Content) : AssistantMessage.CreateAssistant(m.Content);
                msg.IsStreaming = m.IsStreaming;
                msg.IsApplied = m.IsApplied;
                msg.HasDiff = m.HasDiff;
                if (m.DiffLines != null) msg.SetDiff(m.DiffLines);
                pane.Messages.Add(msg);
            }
        }

        NotifyCollectionChangedEventHandler collHandler = (s, e) => SyncMessages();

        pane.SendRequested += sendHandler;
        pane.StopRequested += stopHandler;
        pane.NewConversationRequested += newConvHandler;
        pane.ApplyDiffRequested += applyDiffHandler;
        assistantVm.PropertyChanged += propHandler;
        assistantVm.Messages.CollectionChanged += collHandler;

        pane.Unloaded += (s, e) =>
        {
            pane.SendRequested -= sendHandler;
            pane.StopRequested -= stopHandler;
            pane.NewConversationRequested -= newConvHandler;
            pane.ApplyDiffRequested -= applyDiffHandler;
            assistantVm.PropertyChanged -= propHandler;
            assistantVm.Messages.CollectionChanged -= collHandler;
        };

        SyncMessages();
    }
}
