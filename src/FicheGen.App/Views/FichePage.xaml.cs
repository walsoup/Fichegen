using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;

namespace FicheGen.App.Views;

/// <summary>
/// Espace de génération des fiches pédagogiques : modèles rapides, badges Eduscol,
/// améliorateurs de prompt, validation en ligne et disposition adaptative.
/// </summary>
public sealed partial class FichePage : Page
{
    private const string FormWidthSettingsKey = "FicheGen.FichePage.FormColumnWidth";
    private const string PdfTipSettingsKey = "FicheGen.FichePage.PdfTipShown";

    private const string DysSnippet =
        "Adapte la fiche pour des élèves à besoins éducatifs particuliers (DYS) : consignes courtes et numérotées, " +
        "mise en page aérée, exemples guidés pas à pas et temps de réalisation majoré.";
    private const string ProgressifSnippet =
        "Propose des exercices progressifs en trois niveaux : réactivation des prérequis, entraînement guidé, " +
        "puis approfondissement pour les élèves les plus à l'aise.";
    private const string BilanSnippet =
        "Conclus la fiche par un bilan visuel : carte mentale ou schéma récapitulatif à compléter par l'élève " +
        "pour ancrer les apprentissages.";

    private bool _suppressEnhancerSync;
    private bool _assistantOverlayOpen;

    private sealed class SubjectOption
    {
        public string Icon { get; }
        public string Name { get; }
        public SubjectOption(string icon, string name) { Icon = icon; Name = name; }
        public override string ToString() => Name;
    }

    private static readonly SubjectOption[] SubjectOptions =
    {
        new("📖", "Français"),
        new("📐", "Mathématiques"),
        new("🌍", "Histoire-Géographie"),
        new("🏛️", "Enseignement moral et civique"),
        new("🔬", "Sciences et technologie"),
        new("🌱", "SVT"),
        new("⚗️", "Physique-Chimie"),
        new("⚙️", "Technologie"),
        new("🗣️", "Langues vivantes"),
        new("🎨", "Arts plastiques"),
        new("🎵", "Éducation musicale"),
        new("🏃", "EPS"),
        new("➕", "Autre")
    };

    private static readonly string[] TopicSuggestions =
    {
        "Les fractions simples",
        "La Révolution française",
        "L'accord sujet-verbe",
        "Les tables de multiplication",
        "La photosynthèse",
        "Les homophones grammaticaux",
        "La proportionnalité",
        "Le cycle de l'eau",
        "L'échelle et la lecture de carte",
        "Le présent de l'indicatif",
        "Les aires et les périmètres",
        "La chaîne alimentaire",
        "Le Moyen Âge : châteaux forts et seigneuries",
        "Les nombres décimaux",
        "Les champs lexicaux",
        "La Première Guerre mondiale",
        "Le système solaire",
        "Les figures géométriques",
        "La rédaction d'un texte narratif",
        "Les grandes découvertes"
    };

    public FichePage()
    {
        InitializeComponent();
        SubjectCombo.ItemsSource = SubjectOptions;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ───────────────────────── Chargement / persistance ─────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreFormColumnWidth();
        UpdateCycleBadges();
        ValidateForm();
        MaybeShowPdfTip();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => PersistFormColumnWidth();

    private void RestoreFormColumnWidth()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.TryGetValue(FormWidthSettingsKey, out var raw) && raw is double saved && !double.IsNaN(saved))
            {
                var clamped = Math.Clamp(saved, FormColumn.MinWidth, FormColumn.MaxWidth);
                FormColumn.Width = new GridLength(clamped);
            }
        }
        catch { /* Paramètres indisponibles : conserver la largeur par défaut. */ }
    }

    private void PersistFormColumnWidth()
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[FormWidthSettingsKey] = FormColumn.ActualWidth;
        }
        catch { /* Non bloquant. */ }
    }

    // ───────────────────────── Démarrage rapide ─────────────────────────

    private void QuickStart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag) return;
        var parts = tag.Split('|');
        if (parts.Length < 4) return;

        LevelCombo.SelectedValue = parts[0];
        SubjectCombo.Text = parts[1];
        TopicBox.Text = parts[2];
        if (double.TryParse(parts[3], out var minutes))
            DurationBox.Value = Math.Clamp(minutes, DurationBox.Minimum, DurationBox.Maximum);

        UpdateCycleBadges();
        ValidateForm();
    }

    // ───────────────────────── Badges Cycle Eduscol ─────────────────────────

    private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCycleBadges();
        ValidateForm();
    }

    private void UpdateCycleBadges()
    {
        var level = LevelCombo.SelectedValue as string ?? string.Empty;
        string cycle, focus;
        switch (level)
        {
            case "CP":
            case "CE1":
            case "CE2":
                cycle = "🎓 Cycle 2 · Apprentissages fondamentaux";
                focus = "🧠 Lire, écrire, compter";
                break;
            case "CM1":
            case "CM2":
            case "6e":
                cycle = "🎓 Cycle 3 · Consolidation";
                focus = "🧠 Autonomie et abstraction";
                break;
            case "5e":
            case "4e":
            case "3e":
                cycle = "🎓 Cycle 4 · Approfondissements";
                focus = "🧠 Méthode et pensée critique";
                break;
            default:
                cycle = "🎓 Sélectionnez un niveau";
                focus = "🧠 Compétences du cycle";
                break;
        }
        CycleBadgeText.Text = cycle;
        CompetenceBadgeText.Text = focus;
    }

    // ───────────────────────── Matière ─────────────────────────

    private void SubjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SubjectCombo.SelectedItem is SubjectOption option && SubjectCombo.Text != option.Name)
            SubjectCombo.Text = option.Name;
        ValidateForm();
    }

    private void SubjectCombo_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args) => ValidateForm();

    // ───────────────────────── Sujet (suggestions) ─────────────────────────

    private void TopicBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var query = sender.Text.Trim();
            sender.ItemsSource = string.IsNullOrEmpty(query)
                ? TopicSuggestions.Take(8).ToList()
                : TopicSuggestions.Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
        }
        ValidateForm();
    }

    private void TopicBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string suggestion)
            sender.Text = suggestion;
    }

    private void TopicBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => ValidateForm();

    // ───────────────────────── Durée ─────────────────────────

    private void DurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ValidateForm();

    // ───────────────────────── Améliorateurs de prompt ─────────────────────────

    private static string GetSnippet(string key) => key switch
    {
        "Dys" => DysSnippet,
        "Progressif" => ProgressifSnippet,
        "Bilan" => BilanSnippet,
        _ => string.Empty
    };

    private void Enhancer_Checked(object sender, RoutedEventArgs e) => SetSnippet((sender as FrameworkElement)?.Tag as string, true);
    private void Enhancer_Unchecked(object sender, RoutedEventArgs e) => SetSnippet((sender as FrameworkElement)?.Tag as string, false);

    private void SetSnippet(string? key, bool add)
    {
        if (string.IsNullOrEmpty(key)) return;
        var snippet = GetSnippet(key!);
        if (snippet.Length == 0) return;

        _suppressEnhancerSync = true;
        var text = InstructionsBox.Text ?? string.Empty;
        if (add)
        {
            if (!text.Contains(snippet, StringComparison.Ordinal))
                InstructionsBox.Text = string.IsNullOrWhiteSpace(text) ? snippet : text.TrimEnd() + "\n" + snippet;
        }
        else if (text.Contains(snippet, StringComparison.Ordinal))
        {
            InstructionsBox.Text = text.Replace(snippet, string.Empty).Replace("\n\n", "\n").Trim();
        }
        _suppressEnhancerSync = false;
    }

    private void InstructionsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressEnhancerSync)
        {
            // Si l'enseignant modifie le texte à la main, les bascules restent synchronisées.
            SyncEnhancerToggle(DysEnhancer, DysSnippet);
            SyncEnhancerToggle(ProgressifEnhancer, ProgressifSnippet);
            SyncEnhancerToggle(BilanEnhancer, BilanSnippet);
        }
        ValidateForm();
    }

    private void SyncEnhancerToggle(ToggleButton toggle, string snippet)
    {
        var contains = (InstructionsBox.Text ?? string.Empty).Contains(snippet, StringComparison.Ordinal);
        if (toggle.IsChecked != contains)
            toggle.IsChecked = contains; // SetSnippet est idempotent : aucune boucle.
    }

    // ───────────────────────── Guide PDF ─────────────────────────

    private async void PdfDropZoneControl_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Laisse le temps au sélecteur de fichiers de mettre à jour GuideFilePath.
        await Task.Delay(450);
        ValidateForm();
    }

    private void PdfDropZoneControl_Drop(object sender, DragEventArgs e) => ValidateForm();

    private void MaybeShowPdfTip()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (!values.ContainsKey(PdfTipSettingsKey))
            {
                PdfTeachingTip.Target = PdfDropZoneControl;
                PdfTeachingTip.IsOpen = true;
            }
        }
        catch { /* Astuce non critique. */ }
    }

    private void PdfTeachingTip_ActionButtonClick(TeachingTip sender, object args) => MarkPdfTipShown();
    private void PdfTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args) => MarkPdfTipShown();

    private static void MarkPdfTipShown()
    {
        try { ApplicationData.Current.LocalSettings.Values[PdfTipSettingsKey] = true; }
        catch { /* Ignoré. */ }
    }

    // ───────────────────────── Validation en ligne ─────────────────────────

    private void ValidateForm()
    {
        var missing = new System.Collections.Generic.List<string>();
        if (LevelCombo.SelectedValue is not string level || string.IsNullOrWhiteSpace(level)) missing.Add("le niveau");
        if (string.IsNullOrWhiteSpace(SubjectCombo.Text)) missing.Add("la matière");
        if (string.IsNullOrWhiteSpace(TopicBox.Text)) missing.Add("le sujet de la leçon");

        if (missing.Count > 0)
        {
            FormInfoBar.Title = "Formulaire incomplet";
            FormInfoBar.Message = "Pour générer votre fiche, renseignez " + string.Join(", ", missing) + ".";
            FormInfoBar.Severity = InfoBarSeverity.Error;
            FormInfoBar.IsOpen = true;
            GenButton.IsEnabled = false;
            ToolTipService.SetToolTip(GenButton, "Champs manquants : " + string.Join(", ", missing));
        }
        else if (string.IsNullOrWhiteSpace(PdfDropZoneControl.GuideFilePath))
        {
            FormInfoBar.Title = "Conseil";
            FormInfoBar.Message = "💡 Déposez un programme Eduscol (PDF) pour aligner automatiquement la fiche sur les attendus officiels.";
            FormInfoBar.Severity = InfoBarSeverity.Informational;
            FormInfoBar.IsOpen = true;
            GenButton.IsEnabled = true;
            ToolTipService.SetToolTip(GenButton, null);
        }
        else
        {
            FormInfoBar.IsOpen = false;
            GenButton.IsEnabled = true;
            ToolTipService.SetToolTip(GenButton, null);
        }
    }

    // ───────────────────────── Assistant adaptatif (< 1280 px) ─────────────────────────

    private void AdaptiveStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
    {
        if (e.NewState == WideState)
        {
            _assistantOverlayOpen = false;
            AssistantOverlay.Visibility = Visibility.Collapsed;
            MoveAssistantTo(AssistantInlineHost);
        }
        else if (_assistantOverlayOpen)
        {
            MoveAssistantTo(OverlayHost);
        }
    }

    private void AssistantFab_Click(object sender, RoutedEventArgs e)
    {
        _assistantOverlayOpen = true;
        MoveAssistantTo(OverlayHost);
        AssistantOverlay.Visibility = Visibility.Visible;
    }

    private void OverlayClose_Click(object sender, RoutedEventArgs e) => CloseAssistantOverlay();
    private void OverlayBackdrop_Tapped(object sender, TappedRoutedEventArgs e) => CloseAssistantOverlay();

    private void CloseAssistantOverlay()
    {
        _assistantOverlayOpen = false;
        AssistantOverlay.Visibility = Visibility.Collapsed;
        MoveAssistantTo(AssistantInlineHost);
    }

    private void MoveAssistantTo(Panel host)
    {
        if (AssistantPaneControl.Parent == host) return;
        if (AssistantPaneControl.Parent is Panel current)
            current.Children.Remove(AssistantPaneControl);
        host.Children.Add(AssistantPaneControl);
    }

    // ───────────────────────── Séparateur redimensionnable ─────────────────────────

    private void FormSplitter_PointerEntered(object sender, PointerRoutedEventArgs e) => SplitterGrip.Opacity = 1;
    private void FormSplitter_PointerExited(object sender, PointerRoutedEventArgs e) => SplitterGrip.Opacity = 0.45;
    private void FormSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => PersistFormColumnWidth();
}
