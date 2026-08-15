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
public sealed partial class FichePage : Page, IAssistantHostPage
{
    private const string FormWidthSettingsKey = "FicheGen.FichePage.FormColumnWidth";
    private bool _isAssistantVisible;
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
        AssistantHostHelper.WireAssistantPane(AssistantPaneControl);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ───────────────────────── Chargement / persistance ─────────────────────────

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

    // ───────────────────────── Niveau & Matière ─────────────────────────

    private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ValidateForm();

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

    private async void PdfDropZoneControl_Tapped(object sender, TappedRoutedEventArgs e)
    {
        await Task.Delay(450);
        ValidateForm();
    }

    private void PdfDropZoneControl_Drop(object sender, DragEventArgs e) => ValidateForm();

    // ───────────────────────── Validation en ligne ─────────────────────────

    private void ValidateForm()
    {
        if (LevelCombo == null || SubjectCombo == null || TopicBox == null || FormInfoBar == null)
            return;

        var missing = new System.Collections.Generic.List<string>();
        if (LevelCombo.SelectedValue is not string level || string.IsNullOrWhiteSpace(level)) missing.Add("le niveau");
        if (string.IsNullOrWhiteSpace(SubjectCombo.Text)) missing.Add("la matière");
        if (string.IsNullOrWhiteSpace(TopicBox.Text)) missing.Add("le sujet de la leçon");

        if (missing.Count > 0)
        {
            FormInfoBar.Title = "Informations requises";
            FormInfoBar.Message = "Veuillez renseigner : " + string.Join(", ", missing) + ".";
            FormInfoBar.Severity = InfoBarSeverity.Informational;
            FormInfoBar.IsOpen = true;
            if (CmdGenerate != null) CmdGenerate.IsEnabled = false;
        }
        else
        {
            FormInfoBar.IsOpen = false;
            if (CmdGenerate != null) CmdGenerate.IsEnabled = true;
        }
    }

    private void OnToggleAssistantClicked(object sender, RoutedEventArgs e)
    {
        SetAssistantVisible(!_isAssistantVisible);
    }

    // ───────────────────────── Assistant adaptatif (< 1380 px) ─────────────────────────

    public void SetAssistantVisible(bool isVisible)
    {
        _isAssistantVisible = isVisible;
        if (ActualWidth >= 1380)
        {
            AssistantColumn.Width = isVisible ? new GridLength(360) : new GridLength(0);
            if (!isVisible) CloseAssistantOverlay();
        }
        else
        {
            if (isVisible)
            {
                _assistantOverlayOpen = true;
                MoveAssistantTo(OverlayHost);
                AssistantOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                CloseAssistantOverlay();
            }
        }
    }

    private void AdaptiveStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
    {
        if (e.NewState?.Name == "WideState")
        {
            _assistantOverlayOpen = false;
            AssistantOverlay.Visibility = Visibility.Collapsed;
            MoveAssistantTo(AssistantInlineHost);
            AssistantColumn.Width = _isAssistantVisible ? new GridLength(340) : new GridLength(0);
        }
        else
        {
            AssistantColumn.Width = new GridLength(0);
            if (_assistantOverlayOpen)
            {
                MoveAssistantTo(OverlayHost);
            }
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
