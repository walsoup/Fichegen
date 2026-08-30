using System;
using System.Collections.Generic;
using System.Linq;
using FicheGen.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FicheGen.App.Views;

/// <summary>
/// Espace de génération des évaluations : sélecteur de notions multi-critères avec puces,
/// type (diagnostique / formative / sommative), barème personnalisable, difficulté illustrée.
/// Le squelette 3 colonnes (formulaire / aperçu / assistant) est mutualisé dans
/// <see cref="Controls.CreationWorkspace"/>.
/// </summary>
public sealed partial class EvaluationPage : Page, IAssistantHostPage, ICreationPage, ILocalizablePage
{
    private readonly List<string> _selectedLessons = new();
    private bool _syncingLessons;
    private bool _chipsInitialized;
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
        new("🗣️", "Langues vivantes"),
        new("🎨", "Arts plastiques"),
        new("🎵", "Éducation musicale"),
        new("🏃", "EPS"),
        new("➕", "Autre")
    };

    private static readonly string[] MathLessons =
        { "Les fractions", "Le calcul mental", "Aires et périmètres", "La proportionnalité", "Les nombres décimaux", "Angles et symétrie" };
    private static readonly string[] FrenchLessons =
        { "L'accord sujet-verbe", "Les homophones", "Le présent de l'indicatif", "La lecture-compréhension", "Les champs lexicaux", "L'accord de l'adjectif" };
    private static readonly string[] HistoryLessons =
        { "La Révolution française", "La Première Guerre mondiale", "Le Moyen Âge", "La lecture de carte", "Les grandes découvertes", "La Vème République" };
    private static readonly string[] ScienceLessons =
        { "La photosynthèse", "Le cycle de l'eau", "La chaîne alimentaire", "Les états de la matière", "Le système solaire", "Volcans et séismes" };
    private static readonly string[] EpsLessons =
        { "La course d'endurance", "Le lancer", "L'expression corporelle", "Les sports collectifs", "La natation", "Le parcours d'obstacles" };
    private static readonly string[] DefaultLessons =
        { "Les fractions", "L'accord sujet-verbe", "La Révolution française", "Le cycle de l'eau", "La proportionnalité", "La lecture-compréhension" };

    public EvaluationPage()
    {
        InitializeComponent();
        SubjectCombo.ItemsSource = SubjectOptions;
        SuggestedLessonsRepeater.ItemsSource = DefaultLessons;
        AssistantHostHelper.WireAssistantPane(Workspace.AssistantPaneControl);

        // Garde la bascule Assistant de la barre de titre synchronisée avec l'état
        // réel du volet — sinon il « réapparaît » à la navigation suivante.
        Workspace.AssistantVisibilityChanged += (_, isVisible) =>
            (App.CurrentMainWindow as MainWindow)?.SyncAssistantToggle(isVisible);

        PdfDropZoneControl.RegisterPropertyChangedCallback(
            Controls.PdfDropZone.GuideFilePathProperty, (_, _) => ValidateForm());

        // Un chapitre choisi dans la table des matières du guide préremplit les notions.
        PdfDropZoneControl.ChapterSelected += (_, entry) =>
        {
            if (DataContext is EvaluationViewModel vm && !string.IsNullOrWhiteSpace(entry.Title))
            {
                vm.Topics = entry.Title;
            }
        };

        Loaded += OnLoaded;
    }

    // ───────────────────────── Chargement ─────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshLocalizedStrings();
        if (DataContext is EvaluationViewModel vm)
        {
            if (LevelCombo.SelectedItem == null && !string.IsNullOrWhiteSpace(vm.ClassLevel))
                LevelCombo.SelectedItem = vm.ClassLevel;
            if (string.IsNullOrWhiteSpace(SubjectCombo.Text) && !string.IsNullOrWhiteSpace(vm.Subject))
            {
                SubjectCombo.SelectedItem = SubjectOptions.FirstOrDefault(s => s.Name == vm.Subject);
                SubjectCombo.Text = vm.Subject;
            }
        }
        RefreshLessonSuggestions();
        InitializeChipsFromViewModel();
        UpdateDifficultyLabel();
        Workspace.RunFormEntranceAnimation();
        ValidateForm();
    }

    public void RefreshLocalizedStrings()
    {
        PageTitle.Text = Services.L10n.Get("EP_Title.Text", "Nouvelle évaluation");
        PageSubtitle.Text = Services.L10n.Get("EP_Subtitle.Text", "Créez un contrôle avec exercices progressifs et barème de correction.");
        Step1Header.Text = Services.L10n.Get("EP_Step1_Header.Text", "1. Classe et Matière");
        LevelCombo.Header = Services.L10n.Get("EP_LevelCombo.Header", "Niveau");
        SubjectCombo.Header = Services.L10n.Get("EP_SubjectCombo.Header", "Matière");
        SubjectCombo.PlaceholderText = Services.L10n.Get("EP_SubjectCombo.PlaceholderText", "Sélectionner ou saisir");
        Step2Header.Text = Services.L10n.Get("EP_Step2_Header.Text", "2. Notions à évaluer");
        Step2Sub.Text = Services.L10n.Get("EP_Step2_Sub.Text", "Sélectionnez une ou plusieurs notions ou ajoutez votre propre sujet :");
        NewTagBox.PlaceholderText = Services.L10n.Get("EP_NewTagBox.PlaceholderText", "Autre notion… (ex: Calcul d'angles)");
        AddTagButton.Content = Services.L10n.Get("EP_AddTagButton.Content", "Ajouter");
        Step3Header.Text = Services.L10n.Get("EP_Step3_Header.Text", "3. Format et Barème");
        EvalTypeCombo.Header = Services.L10n.Get("EP_EvalTypeCombo.Header", "Type");
        TotalPointsBox.Header = Services.L10n.Get("EP_TotalPointsBox.Header", "Total points");
        DifficultyHeader.Text = Services.L10n.Get("EP_DifficultyHeader.Text", "Niveau de difficulté");
        Step4Header.Text = Services.L10n.Get("EP_Step4_Header.Text", "4. Guide ou manuel PDF (optionnel)");
        UpdateDifficultyLabel();
    }

    // ───────────────────────── Champs de base ─────────────────────────

    private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ValidateForm();

    private void SubjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SubjectCombo.SelectedItem is SubjectOption option && SubjectCombo.Text != option.Name)
            SubjectCombo.Text = option.Name;
        RefreshLessonSuggestions();
        ValidateForm();
    }

    private void SubjectCombo_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        RefreshLessonSuggestions();
        ValidateForm();
    }

    private void DurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ValidateForm();

    // ───────────────────────── Sélecteur de notions ─────────────────────────

    private static IReadOnlyList<string> GetSuggestions(string? subject)
    {
        var s = (subject ?? string.Empty).ToLowerInvariant();
        if (s.Contains("math")) return MathLessons;
        if (s.Contains("fran") || s.Contains("langue")) return FrenchLessons;
        if (s.Contains("hist") || s.Contains("géo") || s.Contains("geo") || s.Contains("civique")) return HistoryLessons;
        if (s.Contains("scien") || s.Contains("svt") || s.Contains("physique") || s.Contains("techno")) return ScienceLessons;
        if (s.Contains("sport") || s.Contains("eps")) return EpsLessons;
        return DefaultLessons;
    }

    private void RefreshLessonSuggestions()
    {
        SuggestedLessonsRepeater.ItemsSource = GetSuggestions(SubjectCombo.Text);
    }

    private void LessonCheck_Loaded(object sender, RoutedEventArgs e)
    {
        // Restaure l'état coché lorsque la liste de suggestions est reconstruite.
        if (sender is CheckBox cb && cb.Tag is string label)
        {
            _syncingLessons = true;
            cb.IsChecked = _selectedLessons.Contains(label);
            _syncingLessons = false;
        }
    }

    private void LessonCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingLessons || sender is not CheckBox cb || cb.Tag is not string label) return;

        if (cb.IsChecked == true)
        {
            if (!_selectedLessons.Contains(label)) _selectedLessons.Add(label);
        }
        else
        {
            _selectedLessons.Remove(label);
        }
        RefreshLessonsUi();
        ValidateForm();
    }

    private void InitializeChipsFromViewModel()
    {
        var raw = (DataContext as EvaluationViewModel)?.Topics;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = TopicsMirror.Text ?? string.Empty;
        }

        var currentTopics = string.Join(", ", _selectedLessons);
        if (_chipsInitialized && string.Equals(raw.Trim(), currentTopics.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _chipsInitialized = true;
        _selectedLessons.Clear();

        foreach (var item in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0 && !_selectedLessons.Contains(trimmed))
                _selectedLessons.Add(trimmed);
        }
        RefreshLessonsUi();
    }

    private void RefreshLessonsUi()
    {
        SelectedChipsPanel.Children.Clear();
        foreach (var label in _selectedLessons)
            SelectedChipsPanel.Children.Add(CreateChip(label));

        var topicsStr = string.Join(", ", _selectedLessons);
        TopicsMirror.Text = topicsStr;
        if (DataContext is EvaluationViewModel vm)
        {
            vm.Topics = topicsStr;
        }
    }

    private Button CreateChip(string label)
    {
        var chip = new Button
        {
            Style = (Style)Resources["ChipButtonStyle"],
            Tag = label,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                    new FontIcon { Glyph = "\uE8BB", FontSize = 10, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        AutomationProperties.SetName(chip, $"Retirer la notion {label}");
        ToolTipService.SetToolTip(chip, "Retirer cette notion");
        chip.Click += ChipRemove_Click;
        return chip;
    }

    private void ChipRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string label) return;

        _syncingLessons = true;
        UncheckSuggestedLesson(label);
        _selectedLessons.Remove(label);
        _syncingLessons = false;

        RefreshLessonsUi();
        ValidateForm();
    }

    private void UncheckSuggestedLesson(string label)
    {
        var items = GetSuggestions(SubjectCombo.Text);
        for (var i = 0; i < items.Count; i++)
        {
            if (!string.Equals(items[i], label, StringComparison.Ordinal)) continue;
            if (SuggestedLessonsRepeater.TryGetElement(i) is CheckBox cb)
                cb.IsChecked = false;
            break;
        }
    }

    private void NewTagBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AddTagButton.IsEnabled = !string.IsNullOrWhiteSpace(NewTagBox.Text);
    }

    private void NewTagBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddCustomTag();
            e.Handled = true;
        }
    }

    private void AddTag_Click(object sender, RoutedEventArgs e) => AddCustomTag();

    private void AddCustomTag()
    {
        var tag = (NewTagBox.Text ?? string.Empty).Trim();
        if (tag.Length == 0) return;
        if (!_selectedLessons.Contains(tag)) _selectedLessons.Add(tag);
        NewTagBox.Text = string.Empty;
        RefreshLessonsUi();
        ValidateForm();
    }

    // ───────────────────────── Difficulté et barème ─────────────────────────

    private void DifficultySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) => UpdateDifficultyLabel();

    private void UpdateDifficultyLabel()
    {
        var value = (int)Math.Round(double.IsNaN(DifficultySlider.Value) ? 3 : DifficultySlider.Value);
        DifficultyLabel.Text = value switch
        {
            1 => Services.L10n.Get("EP_Difficulty_Discovery", "🌱 Découverte"),
            2 => Services.L10n.Get("EP_Difficulty_Easy", "🌿 Facile"),
            3 => Services.L10n.Get("EP_Difficulty_Intermediate", "🌳 Intermédiaire"),
            4 => Services.L10n.Get("EP_Difficulty_Demanding", "🔥 Exigeant"),
            5 => Services.L10n.Get("EP_Difficulty_Expert", "🏆 Expert"),
            _ => Services.L10n.Get("EP_Difficulty_Intermediate", "🌳 Intermédiaire")
        };
    }

    private void TotalPointsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ValidateForm();

    // ───────────────────────── Guide PDF ─────────────────────────

    // (Validation déclenchée par le callback sur GuideFilePath — voir le constructeur.)

    // ───────────────────────── Validation au moment d'envoyer ─────────────────────────

    private List<string> GetMissingFields()
    {
        var missing = new List<string>();
        if (LevelCombo == null || SubjectCombo == null) return missing;

        var level = LevelCombo.SelectedItem as string
            ?? (LevelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? LevelCombo.SelectedValue as string
            ?? (DataContext as EvaluationViewModel)?.ClassLevel;
        if (string.IsNullOrWhiteSpace(level)) missing.Add(Services.L10n.Get("Validation_MissingLevel"));

        var subject = SubjectCombo.Text;
        if (string.IsNullOrWhiteSpace(subject))
            subject = (SubjectCombo.SelectedItem as SubjectOption)?.Name ?? (DataContext as EvaluationViewModel)?.Subject;
        if (string.IsNullOrWhiteSpace(subject)) missing.Add(Services.L10n.Get("Validation_MissingSubject"));

        var hasTopics = _selectedLessons.Count > 0 || !string.IsNullOrWhiteSpace((DataContext as EvaluationViewModel)?.Topics);
        if (!hasTopics) missing.Add(Services.L10n.Get("Validation_MissingTopics"));

        return missing;
    }

    private void ShowMissingFieldsInfoBar(List<string> missing)
    {
        Workspace.FormInfoBar.Title = Services.L10n.Get("Validation_Title");
        Workspace.FormInfoBar.Message = Services.L10n.Format("EP_MissingFields_Message", string.Join(", ", missing));
        Workspace.FormInfoBar.Severity = InfoBarSeverity.Informational;
        Workspace.FormInfoBar.IsOpen = true;
    }

    private void FocusFirstMissingField(string field)
    {
        FrameworkElement? target = field == Services.L10n.Get("Validation_MissingLevel")
            ? LevelCombo
            : field == Services.L10n.Get("Validation_MissingSubject")
                ? SubjectCombo
                : NewTagBox;
        target?.Focus(FocusState.Keyboard);
    }

    /// <summary>Met à jour l'état du formulaire. Le bouton Générer reste actif pour
    /// expliquer les champs manquants lors d'un clic (F13).</summary>
    private void ValidateForm()
    {
        if (LevelCombo == null || SubjectCombo == null || Workspace.FormInfoBar == null)
            return;

        var missing = GetMissingFields();
        bool generating = DataContext is EvaluationViewModel gvm && gvm.IsGenerating;
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
        if (DataContext is EvaluationViewModel vm)
        {
            if (!string.IsNullOrWhiteSpace(SubjectCombo.Text)) vm.Subject = SubjectCombo.Text;
            var lvl = LevelCombo.SelectedItem as string ?? (LevelCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(lvl)) vm.ClassLevel = lvl;
            if (_selectedLessons.Count > 0) vm.Topics = string.Join(", ", _selectedLessons);
        }

        var missing = GetMissingFields();
        if (missing.Count > 0)
        {
            _hasSubmitAttempted = true;
            ShowMissingFieldsInfoBar(missing);
            FocusFirstMissingField(missing[0]);
            return false;
        }

        if (DataContext is EvaluationViewModel evm && evm.GenerateEvaluationCommand.CanExecute(null))
        {
            Workspace.FormInfoBar.IsOpen = false;
            evm.GenerateEvaluationCommand.Execute(null);
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
        if (DataContext is EvaluationViewModel vm)
        {
            vm.CancelGenerationCommand.Execute(null);
        }
    }

    // ───────────────────────── Assistant adaptatif (délégué au workspace) ─────────────────────────

    public void SetAssistantVisible(bool isVisible) => Workspace.SetAssistantVisible(isVisible);
}
