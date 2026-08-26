using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.Core.Abstractions;
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
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    private readonly HistoryItem _model;
    private readonly string _plainText;
    private string? _levelBadge;

    public HistoryItemViewModel(HistoryItem model, string? searchQuery)
    {
        _model = model;
        Title = string.IsNullOrWhiteSpace(model.Title) ? "Sans titre" : model.Title;
        EditingTitle = Title;
        IsFavorite = model.IsFavorite;

        _plainText = HistoryTextUtilities.ToPlainText(model.Html);
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
        "evaluation" => "Évaluation",
        "quiz" => "Quiz",
        _ => "Fiche"
    };

    public string TypeEmoji => TypeKey switch
    {
        "evaluation" => "📝",
        "quiz" => "⚡",
        _ => "📘"
    };

    public string LevelBadge => _levelBadge ??= HistoryTextUtilities.ExtractLevel(_model.Title, _model.Html);

    public string CreatedTimeDisplay => CreatedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    public string CreatedFullDisplay => CreatedUtc.ToLocalTime().ToString("dddd d MMMM yyyy 'à' HH:mm", FrenchCulture);

    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";

    public string FavoriteActionName => IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris";

    public string FavoriteText => IsFavorite ? "Retirer des favoris" : "Marquer comme favori";

    public string FullAutomationName =>
        $"{TypeDisplay} « {Title} », créé le {CreatedFullDisplay}{(IsFavorite ? ", favori" : string.Empty)}";

    public string SelectionAutomationName => $"Sélectionner le document « {Title} »";

    public string OverflowAutomationName => $"Plus d'actions pour « {Title} »";

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

    /// <summary>"Tout" | "Fiches" | "Évaluations" | "Quiz" | "Favoris".</summary>
    [ObservableProperty]
    public partial string SelectedTypeFilter { get; set; } = "Tout";

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
            ? "Aucun résultat trouvé"
            : "Votre bibliothèque est vide pour l'instant";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool HasResults { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    public partial int SelectedCount { get; set; }

    [ObservableProperty]
    public partial string StatsTotalDisplay { get; set; } = "0 document";

    [ObservableProperty]
    public partial string StatsMonthDisplay { get; set; } = "Aucune génération ce mois-ci";

    [ObservableProperty]
    public partial string StatsFavoritesDisplay { get; set; } = "0 favori";

    public bool HasSelection => SelectedCount > 0;

    public bool ShowEmptyState => !HasResults && !IsLoading;

    public string SelectionSummary => Pluralize(SelectedCount, "document sélectionné", "documents sélectionnés");

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
        old.Cancel();
        old.Dispose();
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
            ? "Chargement de l'historique…"
            : $"Recherche de « {SearchQuery} » en cours…";

        try
        {
            var typeFilter = SelectedTypeFilter switch
            {
                "Fiches" => "fiche",
                "Évaluations" => "evaluation",
                "Quiz" => "quiz",
                _ => null
            };

            var isFavoriteOnly = SelectedTypeFilter == "Favoris";

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
                0 => "Aucun document ne correspond aux critères.",
                1 => "1 document trouvé.",
                var n => $"{n} documents trouvés."
            };
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                StatusMessage = $"Erreur lors du chargement de l'historique : {ex.Message}";
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

        var today = DateTime.UtcNow.Date;
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

            var date = model.CreatedUtc.Date;
            if (date == today) todayItems.Add(vm);
            else if (date == yesterday) yesterdayItems.Add(vm);
            else if (date >= thisWeek) weekItems.Add(vm);
            else olderItems.Add(vm);
        }

        if (todayItems.Count > 0) AddGroup("Aujourd'hui", todayItems);
        if (yesterdayItems.Count > 0) AddGroup("Hier", yesterdayItems);
        if (weekItems.Count > 0) AddGroup("Cette semaine", weekItems);
        if (olderItems.Count > 0) AddGroup("Plus ancien", olderItems);
    }

    private void AddGroup(string header, List<HistoryItemViewModel> items)
    {
        var group = new HistoryGroupViewModel { Header = header };
        foreach (var item in items)
        {
            group.Items.Add(item);
        }

        group.CountDisplay = Pluralize(group.Items.Count, "document", "documents");
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
        StatsTotalDisplay = Pluralize(_allItems.Count, "document", "documents");

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthCount = _allItems.Count(i => i.CreatedUtc >= monthStart);
        StatsMonthDisplay = monthCount == 0
            ? "Aucune génération ce mois-ci"
            : Pluralize(monthCount, "génération ce mois-ci", "générations ce mois-ci");

        StatsFavoritesDisplay = Pluralize(_allItems.Count(i => i.IsFavorite), "favori", "favoris");
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
                group.CountDisplay = Pluralize(group.Items.Count, "document", "documents");
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
    public void OpenItem(HistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;

        // Réhydrate la prévisualisation principale (HTML + preset de style).
        if (!string.IsNullOrEmpty(item.Html))
        {
            _resultViewModel.CurrentHtml = item.Html;
            _resultViewModel.ActivePresetId = item.StylePresetId ?? "modern";
            _resultViewModel.StatusMessage = $"Document « {item.Title} » chargé depuis l'historique.";
        }

        StatusMessage = $"« {item.Title} » ouvert dans le panneau de lecture.";
        DocumentOpened?.Invoke(this, item);
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

        try
        {
            await _historyRepository.DeleteAsync(item.Id);
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
        if (item?.Model is null) return;

        try
        {
            await _historyRepository.SaveAsync(item.Model);
            await LoadHistoryAsync();
            StatusMessage = $"« {item.Title} » restauré dans l'historique.";
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

    private void RaiseExport(HistoryExportFormat format, HistoryItemViewModel? single)
    {
        var items = single is not null
            ? new[] { single }
            : _allItems.Where(i => i.IsSelected).ToArray();

        if (items.Length == 0)
        {
            return;
        }

        ExportRequested?.Invoke(this, new HistoryExportRequestedEventArgs(items, format));
    }
}
