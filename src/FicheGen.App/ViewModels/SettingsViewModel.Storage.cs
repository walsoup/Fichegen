using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using FicheGen.Core.Toc;
using FicheGen.Infrastructure.Pdf;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.System;

namespace FicheGen.App.ViewModels;

public partial class SettingsViewModel
{
    // ────────────── Onglet 3 · Dossiers & Emplacements ───────────────

    [ObservableProperty] public partial string GuidesDir { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExportsDir { get; set; } = string.Empty;
    [ObservableProperty] public partial string GuidesAccessToken { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsGuidesAccessPersistent { get; set; }
    [ObservableProperty] public partial string TocCacheSizeText { get; set; } = "Calcul en cours…";
    [ObservableProperty] public partial bool IsCacheBusy { get; set; }
    [ObservableProperty] public partial bool IsIndexing { get; set; }
    [ObservableProperty] public partial string IndexingProgressText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ParentDocumentsStatsText { get; set; } = "Aucun document indexé — cliquez pour analyser";
    public ObservableCollection<ParentDocumentItem> DiscoveredDocuments { get; } = new();
    [ObservableProperty] public partial ParentDocumentItem? SelectedDocumentToScan { get; set; }
    [ObservableProperty] public partial bool HasDiscoveredDocuments { get; set; }

    private static string TocCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FicheGen", "Cache", "Toc");

    private static string LogsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FicheGen", "Logs");

    // ─────────────── Onglet 5 · Confidentialité ───────────────

    [ObservableProperty] public partial bool TelemetryEnabled { get; set; }
    [ObservableProperty] public partial bool ExpMultiPassGen { get; set; }
    [ObservableProperty] public partial double HistoryRetentionDays { get; set; }
    [ObservableProperty] public partial string DiagnosticLogLevel { get; set; } = "Information";

    public IReadOnlyList<string> LogLevels { get; } = new List<string>
        { "Verbose", "Debug", "Information", "Warning", "Error" };

    // ─────────────── Onglet 6 · Prompts & Raccourcis ───────────────

    public ObservableCollection<PromptTemplateItem> PromptTemplates { get; } = new()
    {
        new PromptTemplateItem("Fiche pédagogique",
            "Génération complète d'une fiche de leçon structurée.",
            "Tu es un enseignant expert du primaire et du collège. Rédige une fiche pédagogique complète pour le niveau {Niveau} en {Matiere}, sur le sujet « {Sujet} ».\n\nStructure attendue :\n1. Objectifs d'apprentissage (3 maximum)\n2. Rappel de cours synthétique\n3. Exercices progressifs (facile → difficile)\n4. Exercice de différenciation pour élèves à besoins particuliers\n5. Corrigé détaillé\n\nConsigne enseignant : {Consigne}"),
        new PromptTemplateItem("Évaluation / contrôle",
            "Création de contrôles notés avec barème.",
            "Crée une évaluation de 20 points pour le niveau {Niveau} en {Matiere} sur « {Sujet} ».\n\nExigences :\n- 4 exercices de difficulté croissante\n- Barème détaillé par question\n- Compétences du socle commun visées\n- Durée indicative : 45 minutes\n- Consigne enseignant : {Consigne}"),
        new PromptTemplateItem("Quiz rapide",
            "QCM de 10 questions avec corrigé.",
            "Génère un quiz de 10 questions à choix multiples (4 options, 1 bonne réponse) pour le niveau {Niveau} en {Matiere} sur « {Sujet} ».\n\nFournis le corrigé commenté en fin de document. {Consigne}"),
        new PromptTemplateItem("Plan de séquence",
            "Progression sur plusieurs séances.",
            "Établis un plan de séquence pédagogique de 5 séances pour le niveau {Niveau} en {Matiere} sur « {Sujet} ».\n\nPour chaque séance : objectif, déroulé minuté, matériel, évaluation formative. {Consigne}"),
    };

    [ObservableProperty] public partial PromptTemplateItem? SelectedPromptTemplate { get; set; }

    public ObservableCollection<KeyboardShortcutItem> Shortcuts { get; } = new()
    {
        new KeyboardShortcutItem("Générer le document", "Ctrl+G"),
        new KeyboardShortcutItem("Nouvelle fiche", "Ctrl+N"),
        new KeyboardShortcutItem("Palette de commandes", "Ctrl+K"),
        new KeyboardShortcutItem("Volet Assistant IA", "Ctrl+B"),
        new KeyboardShortcutItem("Exporter en PDF", "Ctrl+Maj+E"),
        new KeyboardShortcutItem("Exporter en Word (.docx)", "Ctrl+Maj+W"),
        new KeyboardShortcutItem("Imprimer le document", "Ctrl+P"),
        new KeyboardShortcutItem("Rechercher dans l'aperçu", "Ctrl+F"),
        new KeyboardShortcutItem("Annuler la modification", "Ctrl+Z"),
        new KeyboardShortcutItem("Réinitialiser le zoom", "Ctrl+0"),
        new KeyboardShortcutItem("Navigation sections 1 à 4", "Ctrl+1…4"),
        new KeyboardShortcutItem("Annuler / Fermer un volet", "Échap"),
    };

    // ─────────────── Dossiers (FutureAccessList WinRT) ───────────────

    [RelayCommand]
    private async Task BrowseGuidesDirAsync()
    {
        var folder = await _pickerService.PickFolderAsync();
        if (folder is null) return;

        GuidesDir = folder.Path;

        try
        {
            StorageApplicationPermissions.FutureAccessList
                .AddOrReplace("FicheGen.GuidesDir", folder);
            GuidesAccessToken = "FicheGen.GuidesDir";
            IsGuidesAccessPersistent = true;
            StatusMessage = "✅ Dossier des guides enregistré — accès permanent accordé.";
        }
        catch (Exception)
        {
            IsGuidesAccessPersistent = false;
            StatusMessage = "⚠ Dossier enregistré, mais l'accès permanent a échoué.";
        }
    }

    [RelayCommand]
    private async Task BrowseExportsDirAsync()
    {
        var folder = await _pickerService.PickFolderAsync();
        if (folder is null) return;

        ExportsDir = folder.Path;
        try { StorageApplicationPermissions.FutureAccessList.AddOrReplace("FicheGen.ExportsDir", folder); }
        catch { /* non bloquant */ }
    }

    [RelayCommand] private Task OpenGuidesFolderAsync() => OpenFolderAsync(GuidesDir);

    private CancellationTokenSource? _indexingCts;

    [RelayCommand]
    private void CancelIndexing()
    {
        _indexingCts?.Cancel();
    }

    [RelayCommand]
    private async Task ScanParentDocumentsFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(GuidesDir) || !Directory.Exists(GuidesDir))
        {
            StatusMessage = "⚠ Veuillez sélectionner un dossier de documents parents valide.";
            return;
        }

        var indexer = _parentDocumentIndexer ?? App.Services.GetService<ParentDocumentIndexer>();
        if (indexer is null)
        {
            StatusMessage = "⚠ Service d'indexation non disponible.";
            return;
        }

        IsIndexing = true;
        IndexingProgressText = "Recherche récursive des documents PDF…";

        try
        {
            var docs = await Task.Run(() => indexer.DiscoverDocuments(GuidesDir));

            DiscoveredDocuments.Clear();
            foreach (var doc in docs)
            {
                DiscoveredDocuments.Add(doc);
            }

            HasDiscoveredDocuments = DiscoveredDocuments.Count > 0;
            SelectedDocumentToScan = DiscoveredDocuments.FirstOrDefault();

            var totalIndexed = DiscoveredDocuments.Count(d => d.Lessons.Count > 0);
            var totalLessons = DiscoveredDocuments.Sum(d => d.Lessons.Count);

            ParentDocumentsStatsText = $"{DiscoveredDocuments.Count} document(s) trouvé(s) · {totalIndexed} indexé(s) ({totalLessons} leçons)";
            StatusMessage = $"🔍 {DiscoveredDocuments.Count} document(s) trouvé(s) dans le dossier.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠ Erreur lors de la recherche : {ex.Message}";
            Serilog.Log.Warning(ex, "Erreur lors de la recherche des documents parents.");
        }
        finally
        {
            IsIndexing = false;
            IndexingProgressText = string.Empty;
        }
    }

    private Func<string, Task<bool>> CreateConfirmOfflineCallback()
    {
        return async fileName =>
        {
            var mainWindow = App.CurrentMainWindow as MainWindow;
            if (mainWindow == null) return false;

            var tcs = new TaskCompletionSource<bool>();
            var enqueued = mainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        XamlRoot = mainWindow.Content.XamlRoot,
                        Title = "Extraction IA indisponible",
                        Content = $"L'analyse assistée par IA n'a pas pu aboutir pour le document « {fileName} ».\n\nSouhaitez-vous poursuivre avec l'extraction heuristique hors-ligne ?",
                        PrimaryButtonText = "Continuer hors-ligne",
                        CloseButtonText = "Annuler",
                        DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary
                    };
                    var res = await dialog.ShowAsync();
                    tcs.TrySetResult(res == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary);
                }
                catch
                {
                    tcs.TrySetResult(false);
                }
            });

            if (!enqueued)
            {
                tcs.TrySetResult(false);
            }

            return await tcs.Task;
        };
    }

    [RelayCommand]
    private async Task ScanSelectedDocumentTocAsync()
    {
        if (SelectedDocumentToScan is null || string.IsNullOrWhiteSpace(SelectedDocumentToScan.FilePath))
        {
            StatusMessage = "⚠ Veuillez sélectionner un document à analyser.";
            return;
        }

        var indexer = _parentDocumentIndexer ?? App.Services.GetService<ParentDocumentIndexer>();
        if (indexer is null)
        {
            StatusMessage = "⚠ Service d'indexation non disponible.";
            return;
        }

        _indexingCts = new CancellationTokenSource();
        var ct = _indexingCts.Token;

        IsIndexing = true;
        IndexingProgressText = $"Extraction de la table des matières pour {SelectedDocumentToScan.FileName}…";

        try
        {
            var aiConfig = BuildAiConfig();
            var progress = new Progress<string>(msg =>
            {
                IndexingProgressText = msg;
            });

            var confirmOffline = CreateConfirmOfflineCallback();
            var targetPath = SelectedDocumentToScan.FilePath;
            var updatedDoc = await Task.Run(() => indexer.IndexSingleDocumentAndSaveAsync(
                GuidesDir, targetPath, aiConfig, confirmOffline, progress, forceRescan: true, ct: ct), ct);

            if (updatedDoc != null)
            {
                var idx = DiscoveredDocuments.IndexOf(SelectedDocumentToScan);
                if (idx >= 0)
                {
                    DiscoveredDocuments[idx] = updatedDoc;
                    SelectedDocumentToScan = updatedDoc;
                }

                var totalIndexed = DiscoveredDocuments.Count(d => d.Lessons.Count > 0);
                var totalLessons = DiscoveredDocuments.Sum(d => d.Lessons.Count);
                ParentDocumentsStatsText = $"{DiscoveredDocuments.Count} document(s) · {totalIndexed} indexé(s) ({totalLessons} leçons)";
                StatusMessage = $"✅ Table des matières extraite pour « {updatedDoc.DropdownLabel} » ({updatedDoc.Lessons.Count} leçons).";

                var ficheVm = App.Services.GetService<FicheFormViewModel>();
                ficheVm?.RefreshDocumentOptions();

                await RefreshTocCacheSizeAsync();
            }
            else
            {
                StatusMessage = "⚠ Aucune leçon n'a pu être extraite de ce document.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Indexation annulée.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠ Erreur d'extraction : {ex.Message}";
            Serilog.Log.Warning(ex, "Erreur lors de l'extraction de la table des matières.");
        }
        finally
        {
            _indexingCts?.Dispose();
            _indexingCts = null;
            IsIndexing = false;
            IndexingProgressText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ScanAndIndexParentDocumentsAsync()
    {
        if (string.IsNullOrWhiteSpace(GuidesDir) || !Directory.Exists(GuidesDir))
        {
            StatusMessage = "⚠ Veuillez sélectionner un dossier de documents parents valide.";
            return;
        }

        var indexer = _parentDocumentIndexer ?? App.Services.GetService<ParentDocumentIndexer>();
        if (indexer is null)
        {
            StatusMessage = "⚠ Service d'indexation non disponible.";
            return;
        }

        _indexingCts = new CancellationTokenSource();
        var ct = _indexingCts.Token;

        IsIndexing = true;
        IndexingProgressText = "Démarrage de l'indexation…";

        try
        {
            var aiConfig = BuildAiConfig();
            var progress = new Progress<string>(msg =>
            {
                IndexingProgressText = msg;
            });

            var confirmOffline = CreateConfirmOfflineCallback();
            var index = await Task.Run(() => indexer.IndexAllAsync(GuidesDir, aiConfig, confirmOffline, progress, ct), ct);
            var totalLessons = index.Documents.Sum(d => d.Lessons.Count);
            ParentDocumentsStatsText = $"{index.Documents.Count} document(s) indexé(s) · {totalLessons} leçon(s) prête(s)";
            StatusMessage = $"✅ {index.Documents.Count} document(s) indexé(s) ({totalLessons} leçons prêtes).";

            DiscoveredDocuments.Clear();
            foreach (var doc in index.Documents)
            {
                DiscoveredDocuments.Add(doc);
            }
            HasDiscoveredDocuments = DiscoveredDocuments.Count > 0;
            SelectedDocumentToScan = DiscoveredDocuments.FirstOrDefault();

            var ficheVm = App.Services.GetService<FicheFormViewModel>();
            ficheVm?.RefreshDocumentOptions();

            await RefreshTocCacheSizeAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Indexation annulée.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠ Erreur d'indexation : {ex.Message}";
            Serilog.Log.Warning(ex, "Erreur lors de l'indexation des documents parents.");
        }
        finally
        {
            _indexingCts?.Dispose();
            _indexingCts = null;
            IsIndexing = false;
            IndexingProgressText = string.Empty;
        }
    }

    private FicheGen.Core.Ai.AiRequestConfig BuildAiConfig()
    {
        var s = _settingsStore.GetSettings<FicheGen.Core.Storage.AppSettings>();
        var globalProvider = !string.IsNullOrWhiteSpace(GlobalProvider) ? GlobalProvider : s.Ai.GlobalProvider;
        var proxyUrl = !string.IsNullOrWhiteSpace(ProxyBaseUrl) ? ProxyBaseUrl : s.Ai.ProxyBaseUrl;
        var vertexProj = !string.IsNullOrWhiteSpace(VertexProject) ? VertexProject : s.Ai.Vertex.Project;
        var vertexReg = !string.IsNullOrWhiteSpace(VertexRegion) ? VertexRegion : s.Ai.Vertex.Region;

        var routingOverrides = EnableExpertMode
            ? s.Ai.RoutingOverrides.ToDictionary(k => k.Key, v => new FicheGen.Core.Ai.RoutingOverride(v.Value.Provider, v.Value.Model))
            : new Dictionary<string, FicheGen.Core.Ai.RoutingOverride>();

        return new FicheGen.Core.Ai.AiRequestConfig(
            globalProvider,
            s.Ai.Models,
            routingOverrides,
            proxyUrl,
            vertexProj,
            vertexReg,
            new Dictionary<string, double> { { "toc", 0.1 } },
            async (k, ct) =>
            {
                if (k == "supabase_access_token" && _authService != null)
                {
                    return await _authService.GetValidTokenAsync(ct).ConfigureAwait(false);
                }
                return _credentialStore?.Get(k);
            }
        );
    }

    [RelayCommand] private Task OpenExportsFolderAsync() => OpenFolderAsync(ExportsDir);

    [RelayCommand]
    private async Task OpenTocCacheFolderAsync()
    {
        Directory.CreateDirectory(TocCacheDir);
        await OpenFolderAsync(TocCacheDir);
    }

    [RelayCommand]
    private async Task OpenLogsFolderAsync()
    {
        Directory.CreateDirectory(LogsDir);
        await OpenFolderAsync(LogsDir);
    }

    private static async Task OpenFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { await Launcher.LaunchFolderPathAsync(path); }
        catch
        {
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(path);
                await Launcher.LaunchFolderAsync(folder);
            }
            catch { /* dossier inaccessible */ }
        }
    }

    // ─────────────── Cache ToC ───────────────

    public async Task RefreshTocCacheSizeAsync()
    {
        IsCacheBusy = true;
        TocCacheSizeText = "Calcul en cours…";

        var (size, files) = await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(TocCacheDir)) return (0L, 0);
                var infos = new DirectoryInfo(TocCacheDir).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
                return (infos.Sum(f => f.Length), infos.Count);
            }
            catch { return (0L, 0); }
        });

        TocCacheSizeText = files == 0
            ? "Cache vide (0 octet)"
            : $"{FormatBytes(size)} utilisés · {files} fichier{(files > 1 ? "s" : "")} en cache";

        var index = (_parentDocumentIndexer ?? App.Services.GetService<ParentDocumentIndexer>())?.LoadIndex();
        if (index != null && index.Documents.Count > 0)
        {
            var totalLessons = index.Documents.Sum(d => d.Lessons.Count);
            ParentDocumentsStatsText = $"{index.Documents.Count} document(s) indexé(s) · {totalLessons} leçon(s) prête(s)";
            if (DiscoveredDocuments.Count == 0)
            {
                foreach (var doc in index.Documents)
                {
                    DiscoveredDocuments.Add(doc);
                }
                HasDiscoveredDocuments = DiscoveredDocuments.Count > 0;
                SelectedDocumentToScan = DiscoveredDocuments.FirstOrDefault();
            }
        }
        else
        {
            ParentDocumentsStatsText = "Aucun document indexé — cliquez sur « Rechercher les documents »";
        }

        IsCacheBusy = false;
    }

    public async Task ClearTocCacheAsync()
    {
        IsCacheBusy = true;
        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(TocCacheDir))
                    Directory.Delete(TocCacheDir, recursive: true);
                Directory.CreateDirectory(TocCacheDir);
            }
            catch { /* fichiers verrouillés : nouvelle tentative au prochain démarrage */ }
        });

        StatusMessage = "🧹 Cache de la table des matières vidé avec succès.";
        await RefreshTocCacheSizeAsync();
    }

    [RelayCommand]
    private void ShowAbout()
    {
        (App.CurrentMainWindow as MainWindow)?.ShowAboutDialog();
    }

    // ─────────────── Diagnostic ───────────────

    [RelayCommand]
    private async Task ExportDiagnosticBundleAsync()
    {
        if (_diagnosticExporter is null)
        {
            StatusMessage = "⚠ Service d'exportation de diagnostic non disponible.";
            return;
        }

        var choices = new Dictionary<string, IList<string>> { { "Archive Zip", new List<string> { ".zip" } } };
        var saveFile = await _pickerService.PickSaveFileAsync($"profstudio-diag-{DateTime.Now:yyyyMMdd-HHmmss}.zip", choices);
        if (saveFile is null) return;

        StatusMessage = "Génération du pack de diagnostic (nettoyage des secrets)…";
        await _diagnosticExporter.ExportDiagnosticBundleAsync(saveFile.Path);
        StatusMessage = $"✅ Pack de diagnostic exporté : {saveFile.Name} — aucune clé d'API incluse.";
    }

    // ─────────────── Prompts & raccourcis ───────────────

    [RelayCommand]
    private void ResetPromptTemplate()
    {
        if (SelectedPromptTemplate is null) return;
        SelectedPromptTemplate.Content = SelectedPromptTemplate.DefaultContent;
        StatusMessage = $"↺ Modèle « {SelectedPromptTemplate.Name} » réinitialisé — pensez à enregistrer.";
    }

    [RelayCommand]
    private void ResetShortcuts()
    {
        foreach (var shortcut in Shortcuts)
            shortcut.Keys = shortcut.DefaultKeys;
        StatusMessage = "↺ Raccourcis clavier rétablis — pensez à enregistrer.";
    }
}
