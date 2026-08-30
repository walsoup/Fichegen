using System;
using System.Collections.Generic;
using System.Linq;
using FicheGen.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Serilog;
using Windows.Storage;

namespace FicheGen.App.Views.Controls;

/// <summary>
/// Espace de travail 3 colonnes partagé par les pages de création (Fiche, Évaluation, Quiz) :
/// formulaire redimensionnable + CTA, aperçu, Assistant adaptatif (ancré &lt; 1380 px,
/// superposition au-delà) et validation en ligne. La page fournit ses cartes de
/// formulaire via sa propriété de contenu <see cref="FormCards"/> et garde sa logique métier.
/// </summary>
[ContentProperty(Name = "FormCards")]
public sealed partial class CreationWorkspace : UserControl
{
    // Abaissé de 1380 à 1200 px : un écran 1440 px hérite du volet ancré (plus
    // lisible) ; en dessous, la superposition reste nécessaire faute de place.
    private const double AssistantInlineThreshold = 1200;
    private const string DefaultFormWidthSettingsKey = "FicheGen.CreationWorkspace.FormColumnWidth";

    private bool _isAssistantVisible;

    /// <summary>
    /// Cartes de formulaire déclarées par la page hôte en XAML. Propriété de
    /// contenu dédiée : sans elle, les enfants XAML de la page écraseraient
    /// <see cref="UserControl.Content"/> (la racine 3 colonnes du contrôle)
    /// et détruiraient l'aperçu, l'assistant et la validation en ligne.
    /// </summary>
    public IList<UIElement> FormCards { get; } = new List<UIElement>();

    private System.ComponentModel.INotifyPropertyChanged? _subscribedInpc;

    public CreationWorkspace()
    {
        InitializeComponent();

        // Réévalue le placement de l'Assistant (inline < 1380 px vs superposition)
        // à chaque redimensionnement. Différé hors de la passe de layout :
        // SizeChanged est déclenché en plein measure/arrange, et un reparenting
        // à cet instant lève COMException 0x800F1000.
        SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (IsLoaded) ApplyAssistantPlacement();
        });

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        UnsubscribeFromViewModel();
        if (DataContext is System.ComponentModel.INotifyPropertyChanged inpc)
        {
            _subscribedInpc = inpc;
            inpc.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsGenerating")
        {
            DispatcherQueue.TryEnqueue(UpdateGenerationVisualState);
        }
    }

    private void UpdateGenerationVisualState()
    {
        var isGenProp = DataContext?.GetType().GetProperty("IsGenerating");
        if (isGenProp?.GetValue(DataContext) is bool isGen)
        {
            FormPanelRoot.IsHitTestVisible = !isGen;
            FormPanelRoot.Opacity = isGen ? 0.6 : 1.0;
            SetControlsEnabled(FormPanelRoot, !isGen);
        }
    }

    private void UnsubscribeFromViewModel()
    {
        if (_subscribedInpc != null)
        {
            _subscribedInpc.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedInpc = null;
        }
    }

    // ───────────────────────── Pièces nommées exposées à la page hôte ─────────────────────────

    /// <summary>Panneau recevant les cartes de formulaire de la page + l'InfoBar de validation.</summary>
    public StackPanel FormPanel => FormPanelRoot;

    /// <summary>Colonne Formulaire (largeur restaurable / persistable).</summary>
    public ColumnDefinition FormColumn => FormColumnDef;

    /// <summary>Bouton d'action principal (Générer / Arrêter).</summary>
    public GenerateCta GenerateCta => CtaButton;

    /// <summary>InfoBar de validation en ligne (« Presque prêt ! »).</summary>
    public InfoBar FormInfoBar => FormInfoBarControl;

    /// <summary>Hôte d'aperçu du document.</summary>
    public PreviewHost Preview => PreviewHostControl;

    /// <summary>Volet Assistant IA.</summary>
    public AssistantPane AssistantPaneControl => AssistantPaneHost;

    /// <summary>Hôte de l'assistant en mode ancré (grand écran).</summary>
    public Grid AssistantInlineHost => InlineAssistantHost;

    /// <summary>Hôte de l'assistant en superposition (petit écran).</summary>
    public Grid OverlayHost => OverlayHostGrid;

    /// <summary>Couche de superposition complète (voile + panneau 380 px).</summary>
    public Grid AssistantOverlay => AssistantOverlayLayer;

    /// <summary>Bouton flottant d'ouverture de l'assistant (petit écran).</summary>
    public Button AssistantFab => AssistantFabButton;

    // ───────────────────────── Événements relayés depuis le CTA ─────────────────────────

    /// <summary>L'utilisateur demande la génération (bouton ou raccourci).</summary>
    public event EventHandler? GenerateRequested;

    /// <summary>L'utilisateur demande l'annulation de la génération.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>La visibilité du volet Assistant vient de changer (sync shell ↔ pages).</summary>
    public event EventHandler<bool>? AssistantVisibilityChanged;

    // ───────────────────────── Clé de persistance de la largeur ─────────────────────────

    /// <summary>Clé LocalSettings utilisée pour mémoriser la largeur de la colonne formulaire.
    /// Doit être distincte par page ; une valeur par défaut couvre le cas non renseigné.</summary>
    public string FormWidthSettingsKey { get; set; } = DefaultFormWidthSettingsKey;

    // ───────────────────────── Chargement / persistance ─────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SpliceFormCards();
        RestoreFormColumnWidth();
        if (_subscribedInpc == null && DataContext is System.ComponentModel.INotifyPropertyChanged inpc)
        {
            _subscribedInpc = inpc;
            inpc.PropertyChanged += OnViewModelPropertyChanged;
            UpdateGenerationVisualState();
        }
        // Placement de l'Assistant différé hors de la passe de layout : un
        // reparenting pendant Loaded lève COMException 0x800F1000.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsLoaded) ApplyAssistantPlacement();
        });
    }

    /// <summary>Insère les cartes XAML de la page en tête du panneau de formulaire
    /// (au-dessus de l'InfoBar de validation). Une seule fois.</summary>
    private bool _formCardsSpliced;

    private void SpliceFormCards()
    {
        if (_formCardsSpliced || FormCards.Count == 0)
        {
            return;
        }
        _formCardsSpliced = true;
        for (var i = 0; i < FormCards.Count; i++)
        {
            FormPanelRoot.Children.Insert(i, FormCards[i]);
        }
        FormCards.Clear();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PersistFormColumnWidth();
        UnsubscribeFromViewModel();
    }

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

    /// <summary>Joue l'animation d'entrée « cascade » sur les cartes du formulaire de la page.</summary>
    public void RunFormEntranceAnimation()
    {
        UiMotion.StaggeredFadeUp(FormPanelRoot.Children.OfType<FrameworkElement>());
    }

    // ───────────────────────── Assistant adaptatif (< 1380 px) ─────────────────────────

    /// <summary>État courant de visibilité du volet Assistant.</summary>
    public bool IsAssistantVisible => _isAssistantVisible;

    public void SetAssistantVisible(bool isVisible)
    {
        var changed = _isAssistantVisible != isVisible;
        _isAssistantVisible = isVisible;
        // Le placement est différé jusqu'au chargement : un reparenting du volet
        // avant l'attachement de la page à l'arbre visuel (ex. pendant
        // NavigateTo) lève COMException 0x800F1000. L'état est mémorisé puis
        // appliqué par OnLoaded.
        if (IsLoaded)
        {
            ApplyAssistantPlacement();
        }

        if (changed)
        {
            AssistantVisibilityChanged?.Invoke(this, isVisible);
        }
    }

    /// <summary>Applique le placement courant de l'Assistant (inline vs superposition).
    /// Idempotent ; ne doit jamais s'exécuter pendant une passe de layout ni avant Loaded.</summary>
    private void ApplyAssistantPlacement()
    {
        if (ActualWidth >= AssistantInlineThreshold)
        {
            var wasVisible = AssistantColumnDef.Width.Value > 0;
            AssistantColumnDef.Width = _isAssistantVisible ? new GridLength(360) : new GridLength(0);
            // Ramène toujours le volet inline et ferme le voile : sans cela,
            // un élargissement de fenêtre alors que la superposition est ouverte
            // laisse un voile bloqué et une colonne vide.
            CloseAssistantOverlay();

            // Entrée en douceur uniquement lors d'une vraie apparition (pas à
            // chaque redimensionnement de la fenêtre).
            if (_isAssistantVisible && !wasVisible && UiMotion.Enabled)
            {
                UiMotion.FadeUp(AssistantPaneControl, 18);
            }
        }
        else
        {
            if (_isAssistantVisible)
            {
                MoveAssistantTo(OverlayHost);
                AssistantOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                CloseAssistantOverlay();
            }
        }
    }

    private Control? _lastFocusedElementBeforeOverlay;

    private void AssistantFab_Click(object sender, RoutedEventArgs e)
    {
        _lastFocusedElementBeforeOverlay = FocusManager.GetFocusedElement(XamlRoot) as Control;
        SetAssistantVisible(true);
    }

    private void OverlayClose_Click(object sender, RoutedEventArgs e) => SetAssistantVisible(false);
    private void OverlayBackdrop_Tapped(object sender, TappedRoutedEventArgs e) => SetAssistantVisible(false);

    private void CloseAssistantOverlay()
    {
        AssistantOverlay.Visibility = Visibility.Collapsed;
        MoveAssistantTo(AssistantInlineHost);
        _lastFocusedElementBeforeOverlay?.Focus(FocusState.Programmatic);
        _lastFocusedElementBeforeOverlay = null;
    }

    private void MoveAssistantTo(Panel host)
    {
        // Déjà hébergé (même indirectement, ex. via le Border séparateur) : ne
        // touche à rien — un reparenting inutile en pleine disposition crash (COMException).
        if (IsVisualDescendantOf(AssistantPaneControl, host)) return;

        switch (AssistantPaneControl.Parent)
        {
            case Panel currentPanel:
                currentPanel.Children.Remove(AssistantPaneControl);
                break;
            case Border currentBorder:
                currentBorder.Child = null;
                break;
        }

        host.Children.Add(AssistantPaneControl);
    }

    private static bool IsVisualDescendantOf(DependencyObject? child, DependencyObject ancestor)
    {
        var current = child;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static void SetControlsEnabled(DependencyObject parent, bool isEnabled)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Control ctrl)
            {
                ctrl.IsEnabled = isEnabled;
            }
            SetControlsEnabled(child, isEnabled);
        }
    }

    // ───────────────────────── Relais CTA & séparateur ─────────────────────────

    private void CtaButton_GenerateRequested(object? sender, EventArgs e) => GenerateRequested?.Invoke(this, e);

    private void CtaButton_CancelRequested(object? sender, EventArgs e) => CancelRequested?.Invoke(this, e);

    private void FormSplitter_PointerEntered(object sender, PointerRoutedEventArgs e) => SplitterGrip.Opacity = 1;
    private void FormSplitter_PointerExited(object sender, PointerRoutedEventArgs e) => SplitterGrip.Opacity = 0.45;
    private void FormSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => PersistFormColumnWidth();
}
