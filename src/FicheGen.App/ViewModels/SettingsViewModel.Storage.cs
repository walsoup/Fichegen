using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.System;

namespace FicheGen.App.ViewModels;

public partial class SettingsViewModel
{
    // ──────��──────── Onglet 3 · Dossiers & Emplacements ───────────────

    [ObservableProperty] public partial string GuidesDir { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExportsDir { get; set; } = string.Empty;
    [ObservableProperty] public partial string GuidesAccessToken { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsGuidesAccessPersistent { get; set; }
    [ObservableProperty] public partial string TocCacheSizeText { get; set; } = "Calcul en cours…";
    [ObservableProperty] public partial bool IsCacheBusy { get; set; }

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
