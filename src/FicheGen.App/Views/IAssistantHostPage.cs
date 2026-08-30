using System;
using System.Collections.Generic;
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

/// <summary>
/// Interface implémentée par les pages de création (Fiche, Évaluation, Quiz).
/// Permet au shell de déclencher la génération en passant par la validation
/// de la page (message sur le champ manquant + focus), jamais en la contournant.
/// </summary>
public interface ICreationPage
{
    /// <summary>Tente de démarrer la génération. Retourne false si le formulaire est incomplet.</summary>
    bool TryStartGeneration();

    /// <summary>Joue l'animation connectée « carte d'historique → aperçu », si préparée.</summary>
    void RunIncomingDocumentAnimation();

    /// <summary>Réinitialise le zoom de l'aperçu à 100 % (Ctrl+0).</summary>
    void ResetPreviewZoom();
}

public static class AssistantHostHelper
{
    public static void WireAssistantPane(AssistantPane pane)
    {
        var assistantVm = App.Services.GetRequiredService<AssistantViewModel>();

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
            if (e.Message == null) return;
            var index = pane.Messages.IndexOf(e.Message);
            assistantVm.ApplyEditAtIndex(index);
        };

        EventHandler<AssistantDiffEventArgs> rejectDiffHandler = (s, e) =>
        {
            if (e.Message == null) return;
            var index = pane.Messages.IndexOf(e.Message);
            assistantVm.RejectEditAtIndex(index);
        };

        PropertyChangedEventHandler propHandler = (s, e) =>
        {
            if (e.PropertyName == nameof(AssistantViewModel.IsStreaming))
            {
                pane.IsStreaming = assistantVm.IsStreaming;
            }
        };

        // Miroirs des messages du VM : les changements de propriétés au niveau
        // des éléments (contenu en streaming, état « appliqué ») sont répercutés
        // sur la copie affichée, sinon le volet reste figé entre deux envois.
        var itemSubscriptions = new List<(ChatMessageItem Source, PropertyChangedEventHandler Handler)>();

        void DetachItemHandlers()
        {
            foreach (var (source, handler) in itemSubscriptions)
            {
                source.PropertyChanged -= handler;
            }
            itemSubscriptions.Clear();
        }

        void SyncMessages()
        {
            DetachItemHandlers();

            pane.Messages.Clear();
            foreach (var m in assistantVm.Messages)
            {
                var msg = m.IsUser ? AssistantMessage.CreateUser(m.Content) : AssistantMessage.CreateAssistant(m.Content);
                msg.IsStreaming = m.IsStreaming;
                msg.IsApplied = m.IsApplied;
                msg.IsDiffResolved = m.IsDiffResolved;
                msg.HasDiff = m.HasDiff;
                if (m.DiffLines != null) msg.SetDiff(m.DiffLines);

                PropertyChangedEventHandler itemHandler = (_, e) =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(ChatMessageItem.Content):
                            msg.Content = m.Content;
                            break;
                        case nameof(ChatMessageItem.IsStreaming):
                            msg.IsStreaming = m.IsStreaming;
                            break;
                        case nameof(ChatMessageItem.IsApplied):
                            msg.IsApplied = m.IsApplied;
                            break;
                        case nameof(ChatMessageItem.IsDiffResolved):
                            msg.IsDiffResolved = m.IsDiffResolved;
                            break;
                        case nameof(ChatMessageItem.HasDiff):
                            msg.HasDiff = m.HasDiff;
                            break;
                        case nameof(ChatMessageItem.DiffLines):
                            msg.SetDiff(m.DiffLines);
                            break;
                    }
                };
                itemSubscriptions.Add((m, itemHandler));
                m.PropertyChanged += itemHandler;

                pane.Messages.Add(msg);
            }
        }

        NotifyCollectionChangedEventHandler collHandler = (s, e) => SyncMessages();

        // Le volet est déplacé dynamiquement entre l'hôte inline et la couche de
        // superposition ; chaque retrait de l'arbre visuel déclenche Unloaded.
        // On rattache donc à chaque Loaded (gardé idempotent), au lieu d'un
        // abonnement unique à vie qui rendrait le volet muet après un déplacement.
        var wired = false;

        void Attach()
        {
            if (wired) return;
            wired = true;
            pane.SendRequested += sendHandler;
            pane.StopRequested += stopHandler;
            pane.NewConversationRequested += newConvHandler;
            pane.ApplyDiffRequested += applyDiffHandler;
            pane.RejectDiffRequested += rejectDiffHandler;
            assistantVm.PropertyChanged += propHandler;
            assistantVm.Messages.CollectionChanged += collHandler;
            SyncMessages();
        }

        void Detach()
        {
            if (!wired) return;
            wired = false;
            pane.SendRequested -= sendHandler;
            pane.StopRequested -= stopHandler;
            pane.NewConversationRequested -= newConvHandler;
            pane.ApplyDiffRequested -= applyDiffHandler;
            pane.RejectDiffRequested -= rejectDiffHandler;
            assistantVm.PropertyChanged -= propHandler;
            assistantVm.Messages.CollectionChanged -= collHandler;
            DetachItemHandlers();
        }

        pane.Loaded += (_, _) => Attach();
        pane.Unloaded += (_, _) => Detach();

        Attach();
    }
}
