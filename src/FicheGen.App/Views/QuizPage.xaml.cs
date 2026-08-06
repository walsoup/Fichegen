using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;

namespace FicheGen.App.Views;

public sealed partial class QuizPage : Page
{
    private const string FormWidthSettingsKey = "FicheGen.QuizPage.FormColumnWidth";

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
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreFormColumnWidth();
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

    public void SetAssistantPaneVisibility(bool isVisible)
    {
        AssistantColumn.Width = isVisible ? new GridLength(360) : new GridLength(0);
        if (AssistantPaneControl != null)
        {
            AssistantPaneControl.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
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
        ValidateForm();
    }

    private void TopicBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string suggestion)
            sender.Text = suggestion;
    }

    private void TopicBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => ValidateForm();

    private void QuizField_ValueChanged(object sender, RoutedEventArgs e) => ValidateForm();
    private void QuizField_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ValidateForm();

    // ───────────────────────── Validation en ligne ─────────────────────────

    private void ValidateForm()
    {
        if (LevelCombo == null || SubjectCombo == null || TopicBox == null || FormInfoBar == null || GenButton == null) return;

        var missing = new System.Collections.Generic.List<string>();
        if (LevelCombo.SelectedValue is not string level || string.IsNullOrWhiteSpace(level)) missing.Add("le niveau");
        if (string.IsNullOrWhiteSpace(SubjectCombo.Text)) missing.Add("la matière");
        if (string.IsNullOrWhiteSpace(TopicBox.Text)) missing.Add("le sujet du quiz");

        if (missing.Count > 0)
        {
            FormInfoBar.Title = "Formulaire incomplet";
            FormInfoBar.Message = "Pour générer votre quiz, renseignez " + string.Join(", ", missing) + ".";
            FormInfoBar.Severity = InfoBarSeverity.Error;
            FormInfoBar.IsOpen = true;
            GenButton.IsEnabled = false;
            ToolTipService.SetToolTip(GenButton, "Champs manquants : " + string.Join(", ", missing));
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

    private void PdfDropZoneControl_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Laisse le temps au sélecteur de fichiers de mettre à jour.
        _ = Task.Delay(450).ContinueWith(_ => DispatcherQueue.TryEnqueue(ValidateForm));
    }

    private void PdfDropZoneControl_Drop(object sender, DragEventArgs e) => ValidateForm();
}
