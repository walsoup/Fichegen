using System;
using System.Linq;
using FicheGen.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;

namespace FicheGen.App.Views;

/// <summary>
/// Espace de génération des fiches pédagogiques : modèles rapides, badges Eduscol,
/// améliorateurs de prompt, validation au moment d'envoyer et disposition adaptative.
/// Le squelette 3 colonnes (formulaire / aperçu / assistant) est mutualisé dans
/// <see cref="Controls.CreationWorkspace"/> ; cette page ne conserve que ses cartes.
/// </summary>
public sealed partial class FichePage : Page, IAssistantHostPage, ICreationPage, ILocalizablePage
{
    private bool _hasSubmitAttempted;

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
        AssistantHostHelper.WireAssistantPane(Workspace.AssistantPaneControl);

        // Garde la bascule Assistant de la barre de titre synchronisée avec l'état
        // réel du volet — sinon il « réapparaît » à la navigation suivante.
        Workspace.AssistantVisibilityChanged += (_, isVisible) =>
            (App.CurrentMainWindow as MainWindow)?.SyncAssistantToggle(isVisible);

        // Le chemin du guide PDF arrive de façon asynchrôme (picker / glisser-déposer) :
        // on se branche sur la propriété plutôt que sur un délai arbitraire.
        PdfDropZoneControl.RegisterPropertyChangedCallback(
            Controls.PdfDropZone.GuideFilePathProperty, (_, _) => ValidateForm());

        // Un chapitre choisi dans la table des matières du guide préremplit le sujet.
        PdfDropZoneControl.ChapterSelected += (_, entry) =>
        {
            if (DataContext is FicheFormViewModel vm && !string.IsNullOrWhiteSpace(entry.Title))
            {
                vm.Topic = entry.Title;
                TopicBox.Focus(FocusState.Programmatic);
            }
        };

        Loaded += OnLoaded;
    }

    // ───────────────────────── Chargement ─────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshLocalizedStrings();
        if (DataContext is FicheFormViewModel vm)
        {
            vm.RefreshDocumentOptions();
            if (LevelCombo.SelectedItem == null && !string.IsNullOrWhiteSpace(vm.ClassLevel))
            {
                LevelCombo.SelectedItem = vm.ClassLevel;
                LevelCombo.Text = vm.ClassLevel;
            }
            if (string.IsNullOrWhiteSpace(SubjectCombo.Text) && !string.IsNullOrWhiteSpace(vm.Subject))
            {
                SubjectCombo.SelectedItem = SubjectOptions.FirstOrDefault(s => s.Name == vm.Subject);
                SubjectCombo.Text = vm.Subject;
            }
            if (string.IsNullOrWhiteSpace(TopicBox.Text) && !string.IsNullOrWhiteSpace(vm.Topic))
                TopicBox.Text = vm.Topic;
        }
        Workspace.RunFormEntranceAnimation();
        ValidateForm();
    }

    public void RefreshLocalizedStrings()
    {
        PageTitle.Text = Services.L10n.Get("FP_Title.Text", "Nouvelle fiche pédagogique");
        PageSubtitle.Text = Services.L10n.Get("FP_Subtitle.Text", "Préparez une séance structurée, prête à imprimer ou exporter.");
        Step1Header.Text = Services.L10n.Get("FP_Step1_Header.Text", "1. Classe et Matière");
        LevelCombo.Header = Services.L10n.Get("FP_LevelCombo.Header", "Niveau");
        SubjectCombo.Header = Services.L10n.Get("FP_SubjectCombo.Header", "Matière");
        SubjectCombo.PlaceholderText = Services.L10n.Get("FP_SubjectCombo.PlaceholderText", "Sélectionner ou saisir");
        Step2Header.Text = Services.L10n.Get("FP_Step2_Header.Text", "2. Sujet de la séance");
        TopicBox.PlaceholderText = Services.L10n.Get("FP_TopicBox.PlaceholderText", "ex : Les fractions décimales, La Révolution française…");
        CachedLessonsHeader.Text = Services.L10n.Get("FP_CachedLessons_Header.Text", "Leçons du document parent :");
        CachedLessonsCombo.PlaceholderText = Services.L10n.Get("FP_CachedLessons_Placeholder.Text", "Choisir une leçon dans la table des matières…");
        Step3Header.Text = Services.L10n.Get("FP_Step3_Header.Text", "3. Cadre & Durée");
        DurationBox.Header = Services.L10n.Get("FP_DurationBox.Header", "Durée de la séance (minutes)");
        InstructionsBox.Header = Services.L10n.Get("FP_InstructionsBox.Header", "Consignes additionnelles (optionnel)");
        InstructionsBox.PlaceholderText = Services.L10n.Get("FP_InstructionsBox.PlaceholderText", "Précisez un contexte, des besoins DYS, un travail en groupe…");
        Step4Header.Text = Services.L10n.Get("FP_Step4_Header.Text", "4. Appui documentaire PDF (optionnel)");
    }

    // ───────────────────────── Niveau & Matière ─────────────────────────

    private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelCombo.SelectedItem is string selected)
        {
            if (LevelCombo.Text != selected)
                LevelCombo.Text = selected;
            if (DataContext is FicheFormViewModel vm)
                vm.ClassLevel = selected;
        }
        ValidateForm();
    }

    private void LevelCombo_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (DataContext is FicheFormViewModel vm && !string.IsNullOrWhiteSpace(args.Text))
        {
            vm.ClassLevel = args.Text;
        }
        ValidateForm();
    }

    private void CachedLessonsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ValidateForm();
    }

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
        if (DataContext is FicheFormViewModel vm)
        {
            vm.Topic = sender.Text;
        }
        ValidateForm();
    }

    private void TopicBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string suggestion)
        {
            sender.Text = suggestion;
            if (DataContext is FicheFormViewModel vm)
            {
                vm.Topic = suggestion;
            }
        }
        ValidateForm();
    }

    private void TopicBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (DataContext is FicheFormViewModel vm)
        {
            vm.Topic = sender.Text;
        }
        ValidateForm();
    }

    // ───────────────────────── Durée & Consignes ─────────────────────────

    private void DurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ValidateForm();

    private void OnDurationQuickPickClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out var mins))
        {
            DurationBox.Value = mins;
        }
    }

    private void InstructionsBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateForm();

    // ───────────────────────── Guide PDF ─────────────────────────

    // (Validation déclenchée par le callback sur GuideFilePath — voir le constructeur.)

    // ───────────────────────── Validation au moment d'envoyer ─────────────────────────

    private System.Collections.Generic.List<string> GetMissingFields()
    {
        var missing = new System.Collections.Generic.List<string>();
        if (LevelCombo == null || SubjectCombo == null || TopicBox == null) return missing;

        var level = LevelCombo.SelectedItem as string
            ?? (!string.IsNullOrWhiteSpace(LevelCombo.Text) ? LevelCombo.Text : null)
            ?? (LevelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? LevelCombo.SelectedValue as string
            ?? (DataContext as FicheFormViewModel)?.ClassLevel;
        if (string.IsNullOrWhiteSpace(level)) missing.Add(Services.L10n.Get("Validation_MissingLevel"));

        var subject = SubjectCombo.Text;
        if (string.IsNullOrWhiteSpace(subject))
            subject = (SubjectCombo.SelectedItem as SubjectOption)?.Name ?? (DataContext as FicheFormViewModel)?.Subject;
        if (string.IsNullOrWhiteSpace(subject)) missing.Add(Services.L10n.Get("Validation_MissingSubject"));

        var topic = TopicBox.Text;
        if (string.IsNullOrWhiteSpace(topic))
            topic = (DataContext as FicheFormViewModel)?.Topic;
        if (string.IsNullOrWhiteSpace(topic)) missing.Add(Services.L10n.Get("Validation_MissingTopic"));

        return missing;
    }

    private void ShowMissingFieldsInfoBar(System.Collections.Generic.List<string> missing)
    {
        Workspace.FormInfoBar.Title = Services.L10n.Get("Validation_Title");
        Workspace.FormInfoBar.Message = Services.L10n.Format("FP_MissingFields_Message", string.Join(", ", missing));
        Workspace.FormInfoBar.Severity = InfoBarSeverity.Informational;
        Workspace.FormInfoBar.IsOpen = true;
    }

    private void FocusFirstMissingField(string field)
    {
        var target = field == Services.L10n.Get("Validation_MissingLevel")
            ? (FrameworkElement)LevelCombo
            : field == Services.L10n.Get("Validation_MissingSubject")
                ? SubjectCombo
                : TopicBox;
        target?.Focus(FocusState.Keyboard);
    }

    /// <summary>Met à jour l'état du formulaire. Le bouton Générer reste actif pour
    /// expliquer les champs manquants lors d'un clic (F13).</summary>
    private void ValidateForm()
    {
        if (LevelCombo == null || SubjectCombo == null || TopicBox == null || Workspace.FormInfoBar == null)
            return;

        var missing = GetMissingFields();
        bool generating = DataContext is FicheFormViewModel gvm && gvm.IsGenerating;
        if (Workspace.GenerateCta != null)
            Workspace.GenerateCta.IsEnabled = true;

        if (_hasSubmitAttempted)
        {
            if (missing.Count > 0 && !generating) ShowMissingFieldsInfoBar(missing);
            else Workspace.FormInfoBar.IsOpen = false;
        }
    }

    public bool TryStartGeneration()
    {
        if (DataContext is FicheFormViewModel vm)
        {
            if (!string.IsNullOrWhiteSpace(TopicBox.Text)) vm.Topic = TopicBox.Text;
            if (!string.IsNullOrWhiteSpace(SubjectCombo.Text)) vm.Subject = SubjectCombo.Text;
            var lvl = LevelCombo.SelectedItem as string
                ?? (!string.IsNullOrWhiteSpace(LevelCombo.Text) ? LevelCombo.Text : null)
                ?? (LevelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(lvl)) vm.ClassLevel = lvl;
        }

        var missing = GetMissingFields();
        if (missing.Count > 0)
        {
            _hasSubmitAttempted = true;
            ShowMissingFieldsInfoBar(missing);
            FocusFirstMissingField(missing[0]);
            return false;
        }

        if (DataContext is FicheFormViewModel fvm && fvm.GenerateFicheCommand.CanExecute(null))
        {
            Workspace.FormInfoBar.IsOpen = false;
            fvm.GenerateFicheCommand.Execute(null);
            return true;
        }
        return false;
    }

    public void RunIncomingDocumentAnimation()
        => _ = Workspace.Preview.RunIncomingAnimationAsync(Frame);

    public void ResetPreviewZoom()
        => _ = Workspace.Preview.ResetZoomAsync();

    private void GenerateCta_GenerateRequested(object? sender, EventArgs e) => TryStartGeneration();

    private void GenerateCta_CancelRequested(object? sender, EventArgs e)
    {
        if (DataContext is FicheFormViewModel vm)
        {
            vm.CancelGenerationCommand.Execute(null);
        }
    }

    // ───────────────────────── Assistant adaptatif (délégué au workspace) ─────────────────────────

    public void SetAssistantVisible(bool isVisible) => Workspace.SetAssistantVisible(isVisible);
}
