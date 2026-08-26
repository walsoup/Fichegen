// ============================================================================
//  FicheGen — AssistantViewModel
//  Assistant pédagogique conversationnel (génération, modification, questions).
//  Propriétés partielles (WinRT AOT) · Historique de prompts (Haut/Bas)
//  Suggestions intelligentes · Annuler/Rétablir via ResultViewModel (Ctrl+Z)
// ============================================================================

using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Ai;
using FicheGen.Core.Diff;
using FicheGen.Core.Documents;
using FicheGen.Core.Services;
using FicheGen.Core.Storage;
using FicheGen.App.Services;

namespace FicheGen.App.ViewModels;

/// <summary>Message de la conversation avec l'assistant (utilisateur ou IA).</summary>
public partial class ChatMessageItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUser))]
    [NotifyPropertyChangedFor(nameof(RoleLabel))]
    public partial string Sender { get; set; } // "User" | "Assistant"

    [ObservableProperty]
    public partial string Content { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DiffLine>? DiffLines { get; set; }

    [ObservableProperty]
    public partial bool HasDiff { get; set; }

    [ObservableProperty]
    public partial bool IsApplied { get; set; }

    /// <summary><c>true</c> pendant que la réponse de ce message est en cours de diffusion.</summary>
    [ObservableProperty]
    public partial bool IsStreaming { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset Timestamp { get; set; }

    /// <summary>Document révisé reconstruit depuis la réponse IA (prêt à appliquer).</summary>
    public GeneratedDocument? EditedDocument { get; set; }

    public bool IsUser => string.Equals(Sender, "User", StringComparison.Ordinal);
    public string RoleLabel => IsUser ? "Vous" : "Assistant";

    public ChatMessageItem()
    {
        Sender = "User";
        Content = string.Empty;
        Timestamp = DateTimeOffset.Now;
    }
}

/// <summary>Option de mode de l'assistant exposée aux sélecteurs de la vue.</summary>
public sealed record AssistantModeOption(string Value, string Label, string Description);

/// <summary>
/// Vue-modèle de l'assistant pédagogique.
/// Clavier : Ctrl+Entrée (envoyer) · Haut/Bas (historique) · Échap (annuler) · Ctrl+Z (annuler la modification).
/// </summary>
public partial class AssistantViewModel : ObservableObject
{
    private const int MaxPromptHistory = 50;

    private static readonly string[] SuggestionCatalog =
    {
        "Simplifie le vocabulaire pour des élèves en difficulté",
        "Ajoute trois exercices différenciés à la fin du document",
        "Modifie la section « Objectifs » pour l'aligner sur les programmes officiels",
        "Ajoute une activité d'accroche de cinq minutes",
        "Transforme la dernière partie en évaluation formative",
        "Résume le document en cinq points clés pour les élèves",
        "Adapte le document pour des élèves allophones (FLE)",
        "Ajoute deux questions de métacognition en fin de séance",
        "Corrige l'orthographe et la typographie française",
        "Raccourcis les consignes pour qu'elles tiennent en une ligne",
        "Ajoute un tableau de réussite avec des critères observables",
        "Prolonge le document avec une piste de devoir maison"
    };

    private readonly IAssistantService _assistantService;
    private readonly ISettingsStore _settingsStore;
    private readonly ICredentialStore? _credentialStore;
    private readonly ResultViewModel _resultViewModel;

    private readonly List<string> _promptHistory = new();
    private int _historyIndex = -1;
    private string _draftBeforeRecall = string.Empty;
    private CancellationTokenSource? _cts;

    // ------------------------------------------------------------------
    // État
    // ------------------------------------------------------------------

    [ObservableProperty]
    public partial string SelectedMode { get; set; } // "Auto" | "Générer" | "Modifier" | "Question"

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PromptCharacterCount))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial string PromptText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelStreamingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearConversationCommand))]
    public partial bool IsStreaming { get; set; }

    public bool IsIdle => !IsStreaming;

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial StatusSeverity CurrentStatusSeverity { get; set; }

    [ObservableProperty]
    public partial bool AreSuggestionsVisible { get; set; }

    /// <summary>Nombre de caractères du prompt en cours de saisie.</summary>
    public int PromptCharacterCount => PromptText?.Length ?? 0;

    /// <summary>Messages de la conversation (ordre chronologique).</summary>
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    /// <summary>Suggestions intelligentes filtrées en fonction de la saisie.</summary>
    public ObservableCollection<string> PromptSuggestions { get; } = new();

    // ------------------------------------------------------------------
    // Propriétés relayées depuis ResultViewModel
    // ------------------------------------------------------------------

    public bool IsDocumentAvailable => _resultViewModel.CurrentDocument is not null;
    public bool CanUndo => _resultViewModel.CanUndo;
    public bool CanRedo => _resultViewModel.CanRedo;

    public string UndoToolTip => CanUndo ? $"Annuler : {_resultViewModel.UndoDescription} (Ctrl+Z)" : "Rien à annuler";
    public string RedoToolTip => CanRedo ? $"Rétablir : {_resultViewModel.RedoDescription} (Ctrl+Y)" : "Rien à rétablir";

    public string DocumentContextText => _resultViewModel.CurrentDocument is { } doc
        ? $"Contexte : {doc.Metadata.Title} ({doc.Metadata.ClassLevel} · {doc.Metadata.Subject})"
        : "Aucun document chargé — générez d'abord une fiche, une évaluation ou un quiz.";

    public bool IsConversationEmpty => Messages.Count == 0;

    public string MessageCountText => Messages.Count switch
    {
        0 => "Aucun message",
        1 => "1 message",
        _ => $"{Messages.Count} messages"
    };

    // ------------------------------------------------------------------
    // Catalogues exposés à la vue
    // ------------------------------------------------------------------

    public IReadOnlyList<AssistantModeOption> AvailableModes { get; } = new[]
    {
        new AssistantModeOption("Auto", "Automatique", "L'assistant détecte votre intention."),
        new AssistantModeOption("Générer", "Générer", "Produire du nouveau contenu pédagogique."),
        new AssistantModeOption("Modifier", "Modifier", "Transformer le document ouvert."),
        new AssistantModeOption("Question", "Question", "Interroger le document ouvert.")
    };

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } = new[]
    {
        new ShortcutHint("Ctrl+Entrée", "Envoyer le message"),
        new ShortcutHint("Haut / Bas", "Rappeler les prompts précédents"),
        new ShortcutHint("Ctrl+Z", "Annuler la dernière modification du document"),
        new ShortcutHint("Échap", "Interrompre la réponse en cours")
    };

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    public AssistantViewModel(
        IAssistantService assistantService,
        ISettingsStore settingsStore,
        ResultViewModel resultViewModel,
        ICredentialStore? credentialStore = null)
    {
        _assistantService = assistantService;
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _resultViewModel = resultViewModel;

        SelectedMode = "Auto";
        PromptText = string.Empty;
        StatusMessage = "Prêt à vous assister.";
        CurrentStatusSeverity = StatusSeverity.Info;

        Messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsConversationEmpty));
            OnPropertyChanged(nameof(MessageCountText));
        };

        // Relais des changements d'état du document (annuler/rétablir, contexte).
        _resultViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ResultViewModel.CanUndo):
                case nameof(ResultViewModel.CanRedo):
                case nameof(ResultViewModel.UndoDescription):
                case nameof(ResultViewModel.RedoDescription):
                    OnPropertyChanged(nameof(CanUndo));
                    OnPropertyChanged(nameof(CanRedo));
                    OnPropertyChanged(nameof(UndoToolTip));
                    OnPropertyChanged(nameof(RedoToolTip));
                    UndoCommand.NotifyCanExecuteChanged();
                    RedoCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(ResultViewModel.CurrentDocument):
                    OnPropertyChanged(nameof(IsDocumentAvailable));
                    OnPropertyChanged(nameof(DocumentContextText));
                    break;
            }
        };

        RefreshSuggestions(string.Empty);
    }

    // ------------------------------------------------------------------
    // Envoi de message (Ctrl+Entrée)
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendMessageAsync()
    {
        var userMessage = PromptText.Trim();
        PromptText = string.Empty;
        AreSuggestionsVisible = false;

        PushPromptToHistory(userMessage);

        Messages.Add(new ChatMessageItem
        {
            Sender = "User",
            Content = userMessage
        });

        IsStreaming = true;
        SetStatus("L'assistant réfléchit…", StatusSeverity.Info);

        _cts?.Cancel();
        var currentCts = new CancellationTokenSource();
        _cts = currentCts;

        try
        {
            var appSettings = _settingsStore.GetSettings<AppSettings>();
            var config = new AiRequestConfig(
                appSettings.Ai.GlobalProvider,
                appSettings.Ai.Models,
                appSettings.Ai.RoutingOverrides.ToDictionary(k => k.Key, v => new RoutingOverride(v.Value.Provider, v.Value.Model)),
                appSettings.Ai.ProxyBaseUrl,
                appSettings.Ai.Vertex.Project,
                appSettings.Ai.Vertex.Region,
                new Dictionary<string, double> { { "generation", appSettings.Ai.Temperatures.Generation }, { "intent", appSettings.Ai.Temperatures.Intent } },
                (k, _) => ValueTask.FromResult(_credentialStore?.Get(k))
            );

            var assistantMsg = new ChatMessageItem
            {
                Sender = "Assistant",
                Content = string.Empty,
                IsStreaming = true
            };
            Messages.Add(assistantMsg);

            var hasDocument = _resultViewModel.CurrentDocument is not null;
            var wantsEdit = DetectEditIntent(userMessage, hasDocument);

            if (wantsEdit && _resultViewModel.CurrentDocument is not null)
            {
                // Instantané « annuler » capturé AVANT la modification (Ctrl+Z).
                _resultViewModel.PushSnapshot($"Avant : « {Truncate(userMessage, 40)} »");

                SetStatus("L'assistant modifie le document…", StatusSeverity.Info);

                var sb = new StringBuilder();
                await foreach (var chunk in _assistantService.StreamEditAsync(_resultViewModel.CurrentDocument, userMessage, config, currentCts.Token))
                {
                    sb.Append(chunk);
                    assistantMsg.Content = sb.ToString();
                }

                var editedText = sb.ToString();
                if (editedText.Length == 0)
                {
                    assistantMsg.Content = "L'assistant n'a retourné aucune modification.";
                }
                else
                {
                    // Le modèle renvoie le JSON du document révisé : on le reparse en
                    // GeneratedDocument afin que exports, statistiques et annulation
                    // portent sur le contenu réellement édité.
                    GeneratedDocument? editedDoc = null;
                    if (JsonCleaner.TryDeserializeDocument(editedText, out var parsedDoc) && parsedDoc is not null)
                    {
                        editedDoc = parsedDoc;
                    }
                    else
                    {
                        try
                        {
                            editedDoc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(
                                editedText,
                                _resultViewModel.CurrentDocument?.Metadata.Title ?? "Document modifié");
                        }
                        catch (OperationCanceledException) { throw; }
                        catch
                        {
                            // editedDoc reste null : réponse inutilisable.
                        }
                    }

                    if (editedDoc is null)
                    {
                        assistantMsg.Content =
                            "La modification n'a pas pu être convertie en document. " +
                            "Reformulez votre consigne, plus précisément.";
                    }
                    else
                    {
                        assistantMsg.EditedDocument = editedDoc;
                        var originalPlainText = _resultViewModel.CurrentDocument!.ToPlainText();
                        var newPlainText = editedDoc.ToPlainText();
                        assistantMsg.DiffLines = MyersDiffMapper.ComputeDiff(originalPlainText, newPlainText);
                        assistantMsg.HasDiff = true;
                    }
                }
            }
            else if (hasDocument && !string.Equals(SelectedMode, "Générer", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("L'assistant analyse le document…", StatusSeverity.Info);
                var response = await _assistantService.AskQuestionAsync(_resultViewModel.CurrentDocument!, userMessage, config, _cts.Token);
                assistantMsg.Content = response;
            }
            else
            {
                assistantMsg.Content =
                    "Aucun document n'est actuellement ouvert pour cette demande.\n\n" +
                    "Générez d'abord une fiche, une évaluation ou un quiz via les onglets dédiés, " +
                    "puis revenez ici : je peux modifier le document, le simplifier, l'enrichir " +
                    "ou répondre à vos questions à son sujet.";
            }

            assistantMsg.IsStreaming = false;
            SetStatus("Réponse reçue.", StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            if (_cts == currentCts)
            {
                var last = Messages.Count > 0 ? Messages[^1] : null;
                if (last is not null)
                {
                    last.IsStreaming = false;
                    if (last.Sender == "Assistant")
                    {
                        if (last.Content.Length == 0) Messages.Remove(last);
                        else last.Content += "\n\n*(réponse interrompue)*";
                    }
                }
                SetStatus("Traitement annulé par l'utilisateur.", StatusSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            var friendlyMessage = ErrorMessageTranslator.ToUserFriendlyMessage(ex);
            var last = Messages.Count > 0 ? Messages[^1] : null;
            if (last is { Sender: "Assistant" })
            {
                last.IsStreaming = false;
                if (last.Content.Length == 0) last.Content = friendlyMessage;
            }
            SetStatus(friendlyMessage, StatusSeverity.Error);
        }
        finally
        {
            if (_cts == currentCts)
            {
                IsStreaming = false;
                _cts.Dispose();
                _cts = null;
            }
            else
            {
                currentCts.Dispose();
            }
        }
    }

    private bool CanSend() => !IsStreaming && !string.IsNullOrWhiteSpace(PromptText);

    // ------------------------------------------------------------------
    // Différences : appliquer / annuler / rétablir
    // ------------------------------------------------------------------

    /// <summary>Applique les modifications proposées par un message de type diff.</summary>
    [RelayCommand]
    public void ApplyDiff(ChatMessageItem? message)
    {
        if (message?.EditedDocument is null || message.IsApplied) return;
        if (_resultViewModel.CurrentDocument is null) return;

        var label = $"Assistant : « {Truncate(message.Content, 40)} »";
        if (!_resultViewModel.ApplyEditedDocument(message.EditedDocument, label)) return;

        message.IsApplied = true;
        SetStatus("Modifications appliquées au document (Ctrl+Z pour annuler).", StatusSeverity.Success);
    }

    /// <summary>
    /// Applique l'édition affichée à l'index donné du volet (les messages du volet
    /// sont la copie 1:1 de <see cref="Messages"/>).
    /// </summary>
    public void ApplyEditAtIndex(int index)
    {
        if (index < 0 || index >= Messages.Count) return;
        ApplyDiff(Messages[index]);
    }

    /// <summary>Ctrl+Z — Annule la dernière modification du document.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo() => _resultViewModel.Undo();

    /// <summary>Alias conservé pour compatibilité avec les liaisons existantes.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void UndoDiff() => _resultViewModel.Undo();

    /// <summary>Ctrl+Y — Rétablit la modification annulée.</summary>
    [RelayCommand(CanExecute = nameof(CanRedo))]
    public void Redo() => _resultViewModel.Redo();

    // ------------------------------------------------------------------
    // Historique de prompts (Haut / Bas)
    // ------------------------------------------------------------------

    /// <summary>Haut — Rappelle le prompt précédent de la session.</summary>
    [RelayCommand]
    public void RecallPreviousPrompt()
    {
        if (_promptHistory.Count == 0 || IsStreaming) return;

        if (_historyIndex == -1)
        {
            _draftBeforeRecall = PromptText;
            _historyIndex = _promptHistory.Count - 1;
        }
        else if (_historyIndex > 0)
        {
            _historyIndex--;
        }

        PromptText = _promptHistory[_historyIndex];
    }

    /// <summary>Bas — Avance dans l'historique, puis restaure le brouillon de saisie.</summary>
    [RelayCommand]
    public void RecallNextPrompt()
    {
        if (_historyIndex == -1 || IsStreaming) return;

        if (_historyIndex < _promptHistory.Count - 1)
        {
            _historyIndex++;
            PromptText = _promptHistory[_historyIndex];
        }
        else
        {
            _historyIndex = -1;
            PromptText = _draftBeforeRecall;
        }
    }

    private void PushPromptToHistory(string prompt)
    {
        if (_promptHistory.Count == 0 || !string.Equals(_promptHistory[^1], prompt, StringComparison.Ordinal))
        {
            _promptHistory.Add(prompt);
            if (_promptHistory.Count > MaxPromptHistory) _promptHistory.RemoveAt(0);
        }
        _historyIndex = -1;
        _draftBeforeRecall = string.Empty;
    }

    // ------------------------------------------------------------------
    // Suggestions intelligentes de complétion
    // ------------------------------------------------------------------

    partial void OnPromptTextChanged(string value) => RefreshSuggestions(value);

    private void RefreshSuggestions(string? input)
    {
        PromptSuggestions.Clear();

        var query = (input ?? string.Empty).Trim();
        var matches = query.Length < 2
            ? SuggestionCatalog.Take(5)
            : SuggestionCatalog.Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(6);

        foreach (var match in matches) PromptSuggestions.Add(match);

        // Le panneau ne s'affiche que lorsque l'utilisateur a commencé à saisir,
        // sauf ouverture explicite via ToggleSuggestionsCommand.
        AreSuggestionsVisible = PromptSuggestions.Count > 0 && query.Length >= 1;
    }

    /// <summary>Ouvre / ferme manuellement le panneau de suggestions (bouton 💡).</summary>
    [RelayCommand]
    public void ToggleSuggestions()
    {
        if (AreSuggestionsVisible)
        {
            AreSuggestionsVisible = false;
            return;
        }

        PromptSuggestions.Clear();
        foreach (var suggestion in SuggestionCatalog.Take(6)) PromptSuggestions.Add(suggestion);
        AreSuggestionsVisible = true;
    }

    /// <summary>Insère une suggestion dans le champ de saisie.</summary>
    [RelayCommand]
    public void ApplySuggestion(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        PromptText = suggestion;
        AreSuggestionsVisible = false;
        SetStatus("Suggestion insérée — Ctrl+Entrée pour envoyer.", StatusSeverity.Info);
    }

    // ------------------------------------------------------------------
    // Divers
    // ------------------------------------------------------------------

    /// <summary>Efface toute la conversation (hors document).</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    public void ClearConversation()
    {
        Messages.Clear();
        SetStatus("Conversation effacée.", StatusSeverity.Info);
    }

    /// <summary>Échap — Interrompt la diffusion en cours.</summary>
    [RelayCommand(CanExecute = nameof(IsStreaming))]
    public void CancelStreaming() => _cts?.Cancel();

    // ------------------------------------------------------------------
    // Aides internes
    // ------------------------------------------------------------------

    private bool DetectEditIntent(string userMessage, bool hasDocument)
    {
        if (!hasDocument) return false;
        if (string.Equals(SelectedMode, "Modifier", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(SelectedMode, "Question", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(SelectedMode, "Auto", StringComparison.OrdinalIgnoreCase)) return false;

        return userMessage.StartsWith("modifie", StringComparison.OrdinalIgnoreCase)
            || userMessage.StartsWith("remplace", StringComparison.OrdinalIgnoreCase)
            || userMessage.StartsWith("corrige", StringComparison.OrdinalIgnoreCase)
            || userMessage.StartsWith("supprime", StringComparison.OrdinalIgnoreCase)
            || userMessage.StartsWith("ajoute", StringComparison.OrdinalIgnoreCase)
            || userMessage.StartsWith("réécris", StringComparison.OrdinalIgnoreCase)
            || userMessage.StartsWith("réecris", StringComparison.OrdinalIgnoreCase)
            || userMessage.StartsWith("raccourcis", StringComparison.OrdinalIgnoreCase)
            || userMessage.StartsWith("transforme", StringComparison.OrdinalIgnoreCase);
    }

    private void SetStatus(string message, StatusSeverity severity)
    {
        StatusMessage = message;
        CurrentStatusSeverity = severity;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
}
