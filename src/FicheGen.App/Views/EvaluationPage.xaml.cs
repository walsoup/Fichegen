using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.System;

namespace FicheGen.App.Views;

/// <summary>
/// Espace de génération des évaluations : sélecteur de notions multi-critères avec puces,
/// type (diagnostique / formative / sommative), barème personnalisable, difficulté illustrée.
/// </summary>
public sealed partial class EvaluationPage : Page
{
    private const string FormWidthSettingsKey = "FicheGen.EvaluationPage.FormColumnWidth";

    private readonly List<string> _selectedLessons = new();
    private bool _syncingLessons;
    private bool _chipsInitialized;
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
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ───────────────────────── Chargement / persistance ─────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreFormColumnWidth();
        RefreshLessonSuggestions();
        InitializeChipsFromViewModel();
        UpdateDifficultyLabel();
        UpdatePointsBadge(TotalPointsBox.Value);
        ValidateForm();
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
        catch { /* Conserver la largeur par défaut. */ }
    }

    private void PersistFormColumnWidth()
    {
        try { ApplicationData.Current.LocalSettings.Values[FormWidthSettingsKey] = FormColumn.ActualWidth; }
        catch { /* Non bloquant. */ }
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
        if (_chipsInitialized) return;
        _chipsInitialized = true;

        var raw = TopicsMirror.Text ?? string.Empty;
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

        LessonsCountText.Text = _selectedLessons.Count switch
        {
            0 => "Aucune notion sélectionnée",
            1 => "1 notion sélectionnée",
            var n => $"{n} notions sélectionnées"
        };

        TopicsMirror.Text = string.Join(", ", _selectedLessons);
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
            1 => "🌱 Découverte",
            2 => "🌿 Facile",
            3 => "🌳 Intermédiaire",
            4 => "🔥 Exigeant",
            5 => "🏆 Expert",
            _ => "🌳 Intermédiaire"
        };
    }

    private void TotalPointsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => UpdatePointsBadge(args.NewValue);

    private void UpdatePointsBadge(double value)
    {
        if (double.IsNaN(value)) value = 20;
        PointsBadge.Text = $"Évaluation notée sur {value:0} points";
    }

    // ───────────────────────── Guide PDF ─────────────────────────

    private async void PdfDropZoneControl_Tapped(object sender, TappedRoutedEventArgs e)
    {
        await Task.Delay(450);
        ValidateForm();
    }

    private void PdfDropZoneControl_Drop(object sender, DragEventArgs e) => ValidateForm();

    // ───────────────────────── Validation en ligne ─────────────────────────

    private void ValidateForm()
    {
        var missing = new List<string>();
        if (LevelCombo.SelectedValue is not string level || string.IsNullOrWhiteSpace(level)) missing.Add("le niveau");
        if (string.IsNullOrWhiteSpace(SubjectCombo.Text)) missing.Add("la matière");
        if (_selectedLessons.Count == 0) missing.Add("au moins une notion à évaluer");

        if (missing.Count > 0)
        {
            FormInfoBar.Title = "Formulaire incomplet";
            FormInfoBar.Message = "Pour générer l'évaluation, renseignez " + string.Join(", ", missing) + ".";
            FormInfoBar.Severity = InfoBarSeverity.Error;
            FormInfoBar.IsOpen = true;
            GenButton.IsEnabled = false;
            ToolTipService.SetToolTip(GenButton, "Champs manquants : " + string.Join(", ", missing));
        }
        else if (string.IsNullOrWhiteSpace(PdfDropZoneControl.GuideFilePath))
        {
            FormInfoBar.Title = "Conseil";
            FormInfoBar.Message = "💡 Un guide PDF (manuel, progression annuelle) permet de calibrer les exercices sur votre séquence.";
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