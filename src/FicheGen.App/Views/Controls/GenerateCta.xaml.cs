using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace FicheGen.App.Views.Controls;

/// <summary>
/// Bouton d'action principal des pages de création : « Générer avec l'IA » au repos,
/// bascule automatiquement en « Arrêter la génération » pendant le travail du modèle.
/// La page décide de l'action via les événements <see cref="GenerateRequested"/> /
/// <see cref="CancelRequested"/> (validation, focus sur champ manquant…).
/// </summary>
public sealed partial class GenerateCta : UserControl
{
    public static readonly DependencyProperty IsGeneratingProperty =
        DependencyProperty.Register(nameof(IsGenerating), typeof(bool), typeof(GenerateCta),
            new PropertyMetadata(false, OnIsGeneratingChanged));

    /// <summary>True pendant une génération : le bouton devient « Arrêter ».</summary>
    public bool IsGenerating
    {
        get => (bool)GetValue(IsGeneratingProperty);
        set => SetValue(IsGeneratingProperty, value);
    }

    public event EventHandler? GenerateRequested;
    public event EventHandler? CancelRequested;

    public GenerateCta()
    {
        InitializeComponent();
        UpdateVisual();
    }

    private static void OnIsGeneratingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((GenerateCta)d).UpdateVisual();

    private void UpdateVisual()
    {
        bool busy = IsGenerating;

        IdlePanel.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActionButton.Style = (Style)Application.Current.Resources[busy ? "DefaultButtonStyle" : "AccentButtonStyle"];

        AutomationProperties.SetName(ActionButton, busy
            ? Services.L10n.Get("CTA_Name_Busy")
            : Services.L10n.Get("CTA_Name_Idle"));
        ToolTipService.SetToolTip(ActionButton, busy
            ? Services.L10n.Get("CTA_Tooltip_Busy")
            : Services.L10n.Get("CTA_Tooltip_Idle"));
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (IsGenerating) CancelRequested?.Invoke(this, EventArgs.Empty);
        else GenerateRequested?.Invoke(this, EventArgs.Empty);
    }
}
