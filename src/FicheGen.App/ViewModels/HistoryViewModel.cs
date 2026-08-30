using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Documents;
using FicheGen.Core.Services;
using FicheGen.Core.Storage;

namespace FicheGen.App.ViewModels;

// ============================================================================
//  Types auxiliaires de l'espace Historique
// ============================================================================

/// <summary>Formats d'export proposés depuis l'historique.</summary>
public enum HistoryExportFormat
{
    Pdf,
    Word,
    Rtf
}

/// <summary>Arguments de la demande d'export (traitée par la vue : sélecteurs de fichiers).</summary>
public sealed class HistoryExportRequestedEventArgs : EventArgs
{
    public HistoryExportRequestedEventArgs(IReadOnlyList<HistoryItemViewModel> items, HistoryExportFormat format)
    {
        Items = items;
        Format = format;
    }

    public IReadOnlyList<HistoryItemViewModel> Items { get; }
    public HistoryExportFormat Format { get; }
    public bool IsBatch => Items.Count > 1;
}

/// <summary>Groupe chronologique (Aujourd'hui, Hier, Cette semaine, Plus ancien).</summary>
public partial class HistoryGroupViewModel : ObservableObject
{
    public required string Header { get; init; }

    public ObservableCollection<HistoryItemViewModel> Items { get; } = new();

    [ObservableProperty]
    public partial string CountDisplay { get; set; } = string.Empty;
}

/// <summary>
/// Élément d'historique « observable » : enveloppe <see cref="HistoryItem"/> et expose
/// les propriétés calculées d'affichage (badge niveau, extrait de recherche, glyphes…).
/// </summary>
public partial class HistoryItemViewModel : ObservableObject
{
    private readonly HistoryItem _model;
    private readonly string _plainText;
    private string? _levelBadge;

    public HistoryItemViewModel(HistoryItem model, string? searchQuery)
    {
        _model = model;
        Title = string.IsNullOrWhiteSpace(model.Title) ? Services.L10n.Get("History_Untitled", "Sans titre") : model.Title;
        EditingTitle = Title;
        IsFavorite = model.IsFavorite;

        _plainText = !string.IsNullOrWhiteSpace(model.PlainText)
            ? model.PlainText
            : HistoryTextUtilities.ToPlainText(model.Html);
        Snippet = HistoryTextUtilities.BuildSnippet(_plainText, searchQuery);
        HasSnippet = Snippet.Length > 0;
    }

    // ---- Accès modèle -------------------------------------------------------
    public HistoryItem Model => _model;
    public string Id => _model.Id;
    public string? Html => _model.Html;
    public string? StylePresetId => _model.StylePresetId;
    public DateTime CreatedUtc => _model.CreatedUtc;
    public string PlainText => _plainText;

    // ---- Propriétés calculées d'affichage ------------------------------------
    public string TypeKey
    {
        get
        {
            var raw = (_model.Type ?? string.Empty).Trim().ToLowerInvariant();
            return raw switch
            {
                "evaluation" or "évaluation" or "eval" => "evaluation",
                "quiz" or "qcm" => "quiz",
                _ => "fiche"
            };
        }
    }

    public string TypeDisplay => TypeKey switch
    {
        "evaluation" => Services.L10n.Get("History_TypeEvaluation", "Évaluation"),
        "quiz" => Services.L10n.Get("History_TypeQuiz", "Quiz"),
        _ => Services.L10n.Get("History_TypeFiche", "Fiche")
    };

    public string TypeEmoji => TypeKey switch
    {
        "evaluation" => "📝",
        "quiz" => "⚡",
        _ => "📘"
    };

    public string LevelBadge => _levelBadge ??= HistoryTextUtilities.ExtractLevel(_model.Title, !string.IsNullOrEmpty(_plainText) ? _plainText : _model.Html);

    public string CreatedTimeDisplay => CreatedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    public string CreatedFullDisplay => CreatedUtc.ToLocalTime().ToString("f", CultureInfo.CurrentUICulture);

    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";

    public string FavoriteActionName => IsFavorite ? Services.L10n.Get("History_FavoriteRemove", "Retirer des favoris") : Services.L10n.Get("History_FavoriteAdd", "Ajouter aux favoris");

    public string FavoriteText => IsFavorite ? Services.L10n.Get("History_FavoriteRemove", "Retirer des favoris") : Services.L10n.Get("History_FavoriteMark", "Marquer comme favori");

    public string FullAutomationName =>
        Services.L10n.Format("History_AutomationItem", TypeDisplay, Title, CreatedFullDisplay, IsFavorite ? Services.L10n.Get("History_FavoriteSuffix", ", favori") : string.Empty);

    public string SelectionAutomationName => Services.L10n.Format("History_AutomationSelect", Title);

    public string OverflowAutomationName => Services.L10n.Format("History_AutomationMore", Title);

    // ---- Propriétés observables ----------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullAutomationName))]
    [NotifyPropertyChangedFor(nameof(SelectionAutomationName))]
    [NotifyPropertyChangedFor(nameof(OverflowAutomationName))]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string EditingTitle { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
    [NotifyPropertyChangedFor(nameof(FavoriteActionName))]
    [NotifyPropertyChangedFor(nameof(FavoriteText))]
    [NotifyPropertyChangedFor(nameof(FullAutomationName))]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial string Snippet { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSnippet { get; set; }
}

/// <summary>Utilitaires texte : extraction de contenu HTML, badge niveau, extrait de recherche.</summary>
internal static class HistoryTextUtilities
{
    private static readonly Regex BlockTags = new(
        "<\\s*/?\\s*(p|div|br|li|ul|ol|h[1-6]|tr|td|table|section|article|header|footer|blockquote|hr)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AllTags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex MultiSpaces = new("[ \\t]+", RegexOptions.Compiled);
    private static readonly Regex MultiBreaks = new("\\n{3,}", RegexOptions.Compiled);

    private static readonly Regex LevelPattern = new(
        "\\b(CP|CE1|CE2|CM1|CM2|6[eè]me?|5[eè]me?|4[eè]me?|3[eè]me?|2de|seconde|1(?:re|ère)|premi[eè]re|terminale|maternelle|PS|MS|GS)\\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withBreaks = BlockTags.Replace(html, "\n");
        var noTags = AllTags.Replace(withBreaks, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags).Replace('\r', '\n');
        var collapsed = MultiSpaces.Replace(decoded, " ");
        var tidyBreaks = Regex.Replace(collapsed, " ?\\n ?", "\n");
        return MultiBreaks.Replace(tidyBreaks, "\n\n").Trim();
    }

    /// <summary>Badge de niveau « best effort » : détecté dans le titre puis dans le contenu.</summary>
    public static string ExtractLevel(string? title, string? htmlOrText)
    {
        var match = LevelPattern.Match(title ?? string.Empty);
        if (!match.Success && !string.IsNullOrEmpty(htmlOrText))
        {
            var probe = htmlOrText.Length > 800 ? htmlOrText[..800] : htmlOrText;
            match = LevelPattern.Match(probe);
        }

        return match.Success ? CanonicalizeLevel(match.Value) : "Tous niveaux";
    }

    private static string CanonicalizeLevel(string raw) => raw.ToLowerInvariant() switch
    {
        "cp" => "CP",
        "ce1" => "CE1",
        "ce2" => "CE2",
        "cm1" => "CM1",
        "cm2" => "CM2",
        "ps" => "PS",
        "ms" => "MS",
        "gs" => "GS",
        var s when s.StartsWith('6') => "6e",
        var s when s.StartsWith('5') => "5e",
        var s when s.StartsWith('4') => "4e",
        var s when s.StartsWith('3') => "3e",
        "2de" or "seconde" => "2de",
        "terminale" => "Terminale",
        "maternelle" => "Maternelle",
        var s when s.StartsWith('1') || s.StartsWith("premi") => "1re",
        _ => raw
    };

    /// <summary>Construit un extrait centré sur la première occurrence de la requête.</summary>
    public static string BuildSnippet(string plainText, string? query, int maxLength = 160)
    {
        if (string.IsNullOrEmpty(plainText) || string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = -1;
        foreach (var term in terms)
        {
            var i = plainText.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (index < 0 || i < index))
            {
                index = i;
            }
        }

        if (index < 0)
        {
            return string.Empty;
        }

        var start = Math.Max(0, index - 45);
        var length = Math.Min(maxLength, plainText.Length - start);
        var snippet = plainText.Substring(start, length).Trim();
        return (start > 0 ? "… " : string.Empty) + snippet + (start + length < plainText.Length ? " …" : string.Empty);
    }
}

// ============================================================================
//  ViewModel principal de l'historique
// ============================================================================

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryRepository _historyRepository;
    private readonly ResultViewModel _resultViewModel;
    private readonly StylePresetService _stylePresetService;

    private readonly List<HistoryItemViewModel> _allItems = new();
    private CancellationTokenSource? _debounceCts;
    private int _loadVersion;
    private HistoryItem? _lastDeletedFullModel;

    public HistoryViewModel(
        IHistoryRepository historyRepository,
        ResultViewModel resultViewModel,
        StylePresetService stylePresetService)
    {
        _historyRepository = historyRepository;
        _resultViewModel = resultViewModel;
        _stylePresetService = stylePresetService;
    }

    /// <summary>Exposé pour héberger la prévisualisation dans le panneau de lecture.</summary>
    public ResultViewModel ResultViewModel => _resultViewModel;

    public ObservableCollection<HistoryGroupViewModel> GroupedItems { get; } = new();

    /// <summary>Accès direct à la collection des éléments chargés.</summary>
    public IReadOnlyList<HistoryItemViewModel> Items => _allItems;

    /// <summary>Déclenché quand la vue doit exporter un ou plusieurs documents (sélecteurs de fichiers côté vue).</summary>
    public event EventHandler<HistoryExportRequestedEventArgs>? ExportRequested;

    /// <summary>
    /// Déclenché à l'ouverture d'un document : la coquille peut s'y abonner pour
    /// réinitialiser le contexte de conversation de l'assistant et naviguer vers l'espace principal.
    /// </summary>
    public event EventHandler<HistoryItemViewModel>? DocumentOpened;

    // ---- Propriétés observables ----------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    public partial string SearchQuery { get; set; } = string.Empty;

    /// <summary>"all" | "fiche" | "evaluation" | "quiz" | "favorite" (or French legacy strings for backwards compat).</summary>
    [ObservableProperty]
    public partial string SelectedTypeFilter { get; set; } = "all";

    [ObservableProperty]
    public partial HistoryItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>Titre de l'état vide : distingue « aucun document » de « aucun résultat ».</summary>
    public string EmptyStateTitle
        => !string.IsNullOrWhiteSpace(SearchQuery)
            ? Services.L10n.Get("History_NoResultsTitle", "Aucun résultat trouvé")
            : Services.L10n.Get("History_EmptyLibraryTitle", "Votre bibliothèque est vide pour l'instant");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool HasResults { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    public partial int SelectedCount { get; set; }

    [ObservableProperty]
    public partial string StatsTotalDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatsMonthDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatsFavoritesDisplay { get; set; } = string.Empty;

    public bool HasSelection => SelectedCount > 0;

    public bool ShowEmptyState => !HasResults && !IsLoading;

    public string SelectionSummary => Services.L10n.Format("History_SelectionSummary", SelectedCount);

    // ---- Réactions aux changements --------------------------------------------

    partial void OnSearchQueryChanged(string value)
    {
        CancelDebounce();
        var cts = _debounceCts = new CancellationTokenSource();
        _ = DebouncedLoadAsync(cts);
    }

    partial void OnSelectedTypeFilterChanged(string value)
    {
        CancelDebounce();
        _ = LoadHistoryAsync();
    }

    private void CancelDebounce()
    {
        var old = _debounceCts;
        if (old is null) return;
        _debounceCts = null;
        try { old.Cancel(); } catch (ObjectDisposedException) { }
        try { old.Dispose(); } catch (ObjectDisposedException) { }
    }

    private async Task DebouncedLoadAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(400, cts.Token);
            await LoadHistoryAsync();
        }
        catch (OperationCanceledException)
        {
            // Une frappe plus récente a pris le relais.
        }
        finally
        {
            if (ReferenceEquals(_debounceCts, cts))
            {
                _debounceCts = null;
            }
            cts.Dispose();
        }
    }

    /// <summary>Recherche immédiate (validation explicite dans la zone de recherche).</summary>
    public void SubmitSearch()
    {
        CancelDebounce();
        _ = LoadHistoryAsync();
    }

    /// <summary>Suggestions de titres pour l'AutoSuggestBox.</summary>
    public IReadOnlyList<string> GetSuggestions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 2)
        {
            return Array.Empty<string>();
        }

        var trimmed = text.Trim();
        return _allItems
            .Where(i => i.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Title)
            .Distinct()
            .Take(8)
            .ToArray();
    }

    // ---- Chargement & regroupement ---------------------------------------------

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        StatusMessage = string.IsNullOrWhiteSpace(SearchQuery)
            ? Services.L10n.Get("History_Loading", "Chargement de l'historique…")
            : Services.L10n.Format("History_Searching", SearchQuery);

        try
        {
            var filterLower = (SelectedTypeFilter ?? "all").ToLowerInvariant();
            var typeFilter = filterLower switch
            {
                "fiche" or "fiches" => "fiche",
                "evaluation" or "évaluations" or "evaluations" => "evaluation",
                "quiz" => "quiz",
                _ => null
            };

            var isFavoriteOnly = filterLower is "favorite" or "favoris" or "favorites";

            var items = await _historyRepository.SearchAsync(
                query: SearchQuery,
                typeFilter: typeFilter,
                isFavoriteOnly: isFavoriteOnly,
                limit: 200);

            if (version != _loadVersion)
            {
                return; // Une recherche plus récente a déjà pris le relais.
            }

            var previousSelectionId = SelectedItem?.Id;
            RebuildGroups(items);
            UpdateStats();
            HasResults = _allItems.Count > 0;
            SelectedCount = 0;
            SelectedItem = _allItems.FirstOrDefault(i => i.Id == previousSelectionId);

            StatusMessage = _allItems.Count switch
            {
                0 => Services.L10n.Get("History_NoResultsMatching", "Aucun document ne correspond aux critères."),
                1 => Services.L10n.Get("History_OneResultFound", "1 document trouvé."),
                var n => Services.L10n.Format("History_MultipleResultsFound", n)
            };
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                StatusMessage = Services.L10n.Format("History_LoadError", ex.Message);
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private void RebuildGroups(IReadOnlyList<HistoryItem> items)
    {
        foreach (var old in _allItems)
        {
            old.PropertyChanged -= OnItemPropertyChanged;
        }

        _allItems.Clear();
        GroupedItems.Clear();

        var today = DateTime.Now.Date;
        var yesterday = today.AddDays(-1);
        var thisWeek = today.AddDays(-7);

        var todayItems = new List<HistoryItemViewModel>();
        var yesterdayItems = new List<HistoryItemViewModel>();
        var weekItems = new List<HistoryItemViewModel>();
        var olderItems = new List<HistoryItemViewModel>();

        foreach (var model in items)
        {
            var vm = new HistoryItemViewModel(model, SearchQuery);
            vm.PropertyChanged += OnItemPropertyChanged;
            _allItems.Add(vm);

            var localDate = model.CreatedUtc.ToLocalTime().Date;
            if (localDate == today) todayItems.Add(vm);
            else if (localDate == yesterday) yesterdayItems.Add(vm);
            else if (localDate >= thisWeek) weekItems.Add(vm);
            else olderItems.Add(vm);
        }

        if (todayItems.Count > 0) AddGroup(Services.L10n.Get("History_GroupToday", "Aujourd'hui"), todayItems);
        if (yesterdayItems.Count > 0) AddGroup(Services.L10n.Get("History_GroupYesterday", "Hier"), yesterdayItems);
        if (weekItems.Count > 0) AddGroup(Services.L10n.Get("History_GroupThisWeek", "Cette semaine"), weekItems);
        if (olderItems.Count > 0) AddGroup(Services.L10n.Get("History_GroupOlder", "Plus ancien"), olderItems);
    }

    private void AddGroup(string header, List<HistoryItemViewModel> items)
    {
        var group = new HistoryGroupViewModel { Header = header };
        foreach (var item in items)
        {
            group.Items.Add(item);
        }

        group.CountDisplay = Services.L10n.Format("History_GroupCount", group.Items.Count);
        GroupedItems.Add(group);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HistoryItemViewModel.IsSelected):
                SelectedCount = _allItems.Count(static i => i.IsSelected);
                break;
            case nameof(HistoryItemViewModel.IsFavorite):
                UpdateStats();
                break;
        }
    }

    private void UpdateStats()
    {
        StatsTotalDisplay = Services.L10n.Format("History_StatsTotal", _allItems.Count);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthCount = _allItems.Count(i => i.CreatedUtc >= monthStart);
        StatsMonthDisplay = monthCount == 0
            ? Services.L10n.Get("History_StatsMonthEmpty", "Aucune génération ce mois-ci")
            : Services.L10n.Format("History_StatsMonthCount", monthCount);

        StatsFavoritesDisplay = Services.L10n.Format("History_StatsFavorites", _allItems.Count(i => i.IsFavorite));
    }

    private void RemoveItem(HistoryItemViewModel item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
        _allItems.Remove(item);

        for (var i = GroupedItems.Count - 1; i >= 0; i--)
        {
            var group = GroupedItems[i];
            if (group.Items.Remove(item))
            {
                group.CountDisplay = Services.L10n.Format("History_GroupCount", group.Items.Count);
                if (group.Items.Count == 0)
                {
                    GroupedItems.RemoveAt(i);
                }

                break;
            }
        }

        if (SelectedItem?.Id == item.Id)
        {
            SelectedItem = null;
        }

        SelectedCount = _allItems.Count(x => x.IsSelected);
        HasResults = _allItems.Count > 0;
        UpdateStats();
    }

    private static string Pluralize(int count, string singular, string plural)
        => count <= 1 ? $"{count} {singular}" : $"{count} {plural}";

    // ---- Actions unitaires -------------------------------------------------------

    [RelayCommand]
    public async Task OpenItemAsync(HistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;

        var fullModel = item.Model;
        if (string.IsNullOrEmpty(fullModel.SourceJson) && string.IsNullOrEmpty(fullModel.Html))
        {
            var fetched = await _historyRepository.GetByIdAsync(item.Id).ConfigureAwait(true);
            if (fetched != null)
            {
                fullModel = fetched;
            }
        }

        GeneratedDocument? doc = null;
        if (!string.IsNullOrEmpty(fullModel.SourceJson))
        {
            if (JsonCleaner.TryDeserializeDocument(fullModel.SourceJson, out var parsedDoc) && parsedDoc != null)
            {
                doc = parsedDoc;
            }
            else
            {
                try
                {
                    doc = System.Text.Json.JsonSerializer.Deserialize<GeneratedDocument>(fullModel.SourceJson);
                }
                catch { }
            }
        }

        if (doc == null && !string.IsNullOrEmpty(fullModel.PlainText))
        {
            doc = FallbackMarkdownRenderer.ConvertMarkdownToDocument(fullModel.PlainText, fullModel.Title, includeWarningCallout: false);
        }

        if (doc != null)
        {
            _resultViewModel.LoadDocument(doc, fullModel.Html ?? string.Empty, fullModel.StylePresetId);
        }
        else if (!string.IsNullOrEmpty(fullModel.Html))
        {
            _resultViewModel.CurrentHtml = fullModel.Html;
            _resultViewModel.ActivePresetId = fullModel.StylePresetId ?? "modern";
            _resultViewModel.StatusMessage = $"Document « {fullModel.Title} » chargé depuis l'historique.";
        }

        StatusMessage = $"« {item.Title} » ouvert dans le panneau de lecture.";
        DocumentOpened?.Invoke(this, new HistoryItemViewModel(fullModel, SearchQuery));
    }

    public void OpenItem(HistoryItemViewModel? item)
    {
        _ = OpenItemAsync(item);
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync(HistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var newFavorite = !item.IsFavorite;
        item.IsFavorite = newFavorite; // Mise à jour optimiste.

        try
        {
            await _historyRepository.ToggleFavoriteAsync(item.Id, newFavorite);
        }
        catch (Exception ex)
        {
            item.IsFavorite = !newFavorite; // Restauration en cas d'échec.
            StatusMessage = $"Impossible de modifier le favori : {ex.Message}";
            return;
        }

        if (SelectedTypeFilter == "Favoris" && !newFavorite)
        {
            RemoveItem(item);
        }

        UpdateStats();
        StatusMessage = newFavorite
            ? $"« {item.Title} » ajouté aux favoris."
            : $"« {item.Title} » retiré des favoris.";
    }

    [RelayCommand]
    public async Task DeleteItemAsync(HistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        // Hydrate the full model before deleting so restore/undo retains all fields byte-for-byte
        var fullModel = await _historyRepository.GetByIdAsync(item.Id).ConfigureAwait(false) ?? item.Model;
        _lastDeletedFullModel = fullModel;

        try
        {
            await _historyRepository.DeleteAsync(item.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Échec de la suppression : {ex.Message}";
            return;
        }

        var title = item.Title;
        RemoveItem(item);
        StatusMessage = $"« {title} » supprimé de l'historique.";
    }

    [RelayCommand]
    public async Task RestoreItemAsync(HistoryItemViewModel? item)
    {
        var modelToRestore = (_lastDeletedFullModel != null && (item == null || _lastDeletedFullModel.Id == item.Id))
            ? _lastDeletedFullModel
            : item?.Model;

        if (modelToRestore is null) return;

        try
        {
            await _historyRepository.SaveAsync(modelToRestore).ConfigureAwait(false);
            _lastDeletedFullModel = null;
            await LoadHistoryAsync().ConfigureAwait(false);
            StatusMessage = $"« {modelToRestore.Title} » restauré dans l'historique.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Échec de la restauration : {ex.Message}";
        }
    }

    // ---- Renommage en ligne ------------------------------------------------------

    [RelayCommand]
    public void BeginRename(HistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.EditingTitle = item.Title;
        item.IsEditing = true;
    }

    [RelayCommand]
    public async Task CommitRenameAsync(HistoryItemViewModel? item)
    {
        if (item is null || !item.IsEditing)
        {
            return;
        }

        var newTitle = item.EditingTitle.Trim();
        item.IsEditing = false;

        if (string.IsNullOrEmpty(newTitle) || string.Equals(newTitle, item.Title, StringComparison.Ordinal))
        {
            item.EditingTitle = item.Title;
            return;
        }

        var previousTitle = item.Title;
        item.Title = newTitle; // Mise à jour optimiste.

        try
        {
            // Nécessite IHistoryRepository.RenameAsync(string id, string newTitle)
            // (UPDATE ... SET Title = @title WHERE Id = @id).
            await _historyRepository.RenameAsync(item.Id, newTitle);
            StatusMessage = $"Document renommé en « {newTitle} ».";
        }
        catch (Exception ex)
        {
            item.Title = previousTitle;
            item.EditingTitle = previousTitle;
            StatusMessage = $"Échec du renommage : {ex.Message}";
        }
    }

    [RelayCommand]
    public void CancelRename(HistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.EditingTitle = item.Title;
        item.IsEditing = false;
    }

    public async Task<bool> RenameItemAsync(HistoryItemViewModel? item, string newTitle)
    {
        if (item is null) return false;
        var trimmed = newTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, item.Title, StringComparison.Ordinal))
        {
            return false;
        }

        var previousTitle = item.Title;
        item.Title = trimmed;
        item.EditingTitle = trimmed;

        try
        {
            await _historyRepository.RenameAsync(item.Id, trimmed);
            StatusMessage = $"Document renommé en « {trimmed} ».";
            return true;
        }
        catch (Exception ex)
        {
            item.Title = previousTitle;
            item.EditingTitle = previousTitle;
            StatusMessage = $"Échec du renommage : {ex.Message}";
            return false;
        }
    }

    // ---- Sélection multiple --------------------------------------------------------

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in _allItems)
        {
            item.IsSelected = false;
        }

        StatusMessage = "Sélection effacée.";
    }

    // ---- Exports (délégués à la vue via événement) -----------------------------------

    [RelayCommand]
    private void ExportPdf(HistoryItemViewModel? item) => RaiseExport(HistoryExportFormat.Pdf, item);

    [RelayCommand]
    private void ExportWord(HistoryItemViewModel? item) => RaiseExport(HistoryExportFormat.Word, item);

    [RelayCommand]
    private void ExportRtf(HistoryItemViewModel? item) => RaiseExport(HistoryExportFormat.Rtf, item);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ExportSelectionPdf() => RaiseExport(HistoryExportFormat.Pdf, single: null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ExportSelectionWord() => RaiseExport(HistoryExportFormat.Word, single: null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ExportSelectionRtf() => RaiseExport(HistoryExportFormat.Rtf, single: null);

    private async void RaiseExport(HistoryExportFormat format, HistoryItemViewModel? single)
    {
        var rawItems = single is not null
            ? new[] { single }
            : _allItems.Where(i => i.IsSelected).ToArray();

        if (rawItems.Length == 0)
        {
            return;
        }

        var hydratedItems = new List<HistoryItemViewModel>(rawItems.Length);
        foreach (var item in rawItems)
        {
            if (string.IsNullOrEmpty(item.Model.SourceJson) && string.IsNullOrEmpty(item.Model.Html))
            {
                var fullModel = await _historyRepository.GetByIdAsync(item.Id).ConfigureAwait(true) ?? item.Model;
                hydratedItems.Add(new HistoryItemViewModel(fullModel, SearchQuery));
            }
            else
            {
                hydratedItems.Add(item);
            }
        }

        ExportRequested?.Invoke(this, new HistoryExportRequestedEventArgs(hydratedItems, format));
    }
}
