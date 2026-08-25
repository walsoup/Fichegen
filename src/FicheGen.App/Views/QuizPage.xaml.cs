using System;
using System.Linq;
using FicheGen.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FicheGen.App.Views;

/// <summary>
/// Espace de génération des quiz. Le squelette 3 colonnes (formulaire / aperçu /
/// assistant) est mutualisé dans <see cref="Controls.CreationWorkspace"/>.
/// </summary>
public sealed partial class QuizPage : Page, IAssistantHostPage, ICreationPage
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
        "Le cycle de l'eau",
        "Les homophones grammaticaux",
        "La proportionnalité",
        "Les nombres décimaux",
        "Le présent de l'indicatif",
        "Les grandes découvertes"
    };

    public QuizPage()
    {
        InitializeComponent();
        SubjectCombo.ItemsSource = SubjectOptions;
        AssistantHostHelper.WireAssistantPane(Workspace.AssistantPaneControl);

        // Garde la bascule Assistant de la barre de titre synchronisée avec l'état
        // réel du volet — sinon il « réapparaît » à la navigation suivante.
        Workspace.AssistantVisibilityChanged += (_, isVisible) =>
            (App.CurrentMainWindow as MainWindow)?.SyncAssistantToggle(isVisible);

        PdfDropZoneControl.RegisterPropertyChangedCallback(
            Controls.PdfDropZone.GuideFilePathProperty, (_, _) => ValidateForm());

        // Un chapitre choisi dans la table des matières du guide préremplit le sujet.
        PdfDropZoneControl.ChapterSelected += (_, entry) =>
        {
            if (DataContext is QuizViewModel vm && !string.IsNullOrWhiteSpace(entry.Title))
            {
                vm.Topic = entry.Title;
            }
        };

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Workspace.RunFormEntranceAnimation();
        ValidateForm();
    }

    // ───────────────────────── Événements Champs Formulaire ─────────────────────────

    private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ValidateForm();

    private void SubjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SubjectCombo.SelectedItem is SubjectOption option && SubjectCombo.Text != option.Name)
            SubjectCombo.Text = option.Name;
        ValidateForm();
    }

    private void SubjectCombo_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args) => ValidateForm();

    private void TopicBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var query = sender.Text.Trim();
            sender.ItemsSource = string.IsNullOrEmpty(query)
                ? TopicSuggestions.Take(8).ToList()
                : TopicSuggestions.Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
        }
        if (DataContext is QuizViewModel vm)
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
            if (DataContext is QuizViewModel vm)
            {
                vm.Topic = suggestion;
            }
        }
        ValidateForm();
    }

    private void TopicBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (DataContext is QuizViewModel vm)
        {
            vm.Topic = sender.Text;
        }
        ValidateForm();
    }

    private void QuizField_ValueChanged(object sender, RoutedEventArgs e) => ValidateForm();
    private void QuizNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ValidateForm();

    // ───────────────────────── Validation au moment d'envoyer ─────────────────────────

    private System.Collections.Generic.List<string> GetMissingFields()
    {
        var missing = new System.Collections.Generic.List<string>();
        if (LevelCombo == null || SubjectCombo == null || TopicBox == null) return missing;

        var level = LevelCombo.SelectedValue as string ?? LevelCombo.SelectedItem as string ?? (DataContext as QuizViewModel)?.ClassLevel;
        if (string.IsNullOrWhiteSpace(level)) missing.Add(Services.L10n.Get("Validation_MissingLevel"));

        var subject = SubjectCombo.Text;
        if (string.IsNullOrWhiteSpace(subject)) subject = (DataContext as QuizViewModel)?.Subject;
        if (string.IsNullOrWhiteSpace(subject)) missing.Add(Services.L10n.Get("Validation_MissingSubject"));

        var topic = TopicBox.Text;
        if (string.IsNullOrWhiteSpace(topic)) topic = (DataContext as QuizViewModel)?.Topic;
        if (string.IsNullOrWhiteSpace(topic)) missing.Add(Services.L10n.Get("Validation_MissingTopic"));

        return missing;
    }

    private void ShowMissingFieldsInfoBar(System.Collections.Generic.List<string> missing)
    {
        Workspace.FormInfoBar.Title = Services.L10n.Get("Validation_Title");
        Workspace.FormInfoBar.Message = Services.L10n.Format("QP_MissingFields_Message", string.Join(", ", missing));
        Workspace.FormInfoBar.Severity = InfoBarSeverity.Informational;
        Workspace.FormInfoBar.IsOpen = true;
    }

    private void FocusFirstMissingField(string field)
    {
        FrameworkElement? target = field == Services.L10n.Get("Validation_MissingLevel")
            ? LevelCombo
            : field == Services.L10n.Get("Validation_MissingSubject")
                ? SubjectCombo
                : TopicBox;
        target?.Focus(FocusState.Keyboard);
    }

    /// <summary>Met à jour l'état du bouton Générer. L'InfoBar n'apparaît
    /// qu'après une tentative d'envoi — jamais en pleine saisie.</summary>
    private void ValidateForm()
    {
        if (LevelCombo == null || SubjectCombo == null || TopicBox == null || Workspace.FormInfoBar == null) return;

        var missing = GetMissingFields();

        // Pendant une génération le bouton affiche « Arrêter » : il doit rester actif.
        bool generating = DataContext is QuizViewModel gvm && gvm.IsGenerating;
        if (Workspace.GenerateCta != null)
            Workspace.GenerateCta.IsEnabled = generating || missing.Count == 0;

        if (_hasSubmitAttempted)
        {
            if (missing.Count > 0 && !generating) ShowMissingFieldsInfoBar(missing);
            else Workspace.FormInfoBar.IsOpen = false;
        }
    }

    public bool TryStartGeneration()
    {
        var missing = GetMissingFields();
        if (missing.Count > 0)
        {
            _hasSubmitAttempted = true;
            ShowMissingFieldsInfoBar(missing);
            FocusFirstMissingField(missing[0]);
            return false;
        }

        if (DataContext is QuizViewModel vm && vm.GenerateQuizCommand.CanExecute(null))
        {
            Workspace.FormInfoBar.IsOpen = false;
            vm.GenerateQuizCommand.Execute(null);
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
        if (DataContext is QuizViewModel vm)
        {
            vm.CancelGenerationCommand.Execute(null);
        }
    }

    private void OnToggleAssistantClicked(object sender, RoutedEventArgs e)
    {
        Workspace.SetAssistantVisible(!Workspace.IsAssistantVisible);
    }

    // ───────────────────────── Assistant adaptatif (délégué au workspace) ─────────────────────────

    public void SetAssistantVisible(bool isVisible) => Workspace.SetAssistantVisible(isVisible);
}
