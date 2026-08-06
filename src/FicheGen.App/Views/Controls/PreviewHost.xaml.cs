using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace FicheGen.App.Views.Controls;

/// <summary>États d'affichage de l'hôte d'aperçu.</summary>
public enum PreviewState
{
    /// <summary>Aucun document : illustration de marque + astuces.</summary>
    Empty,
    /// <summary>Génération en cours : squelette shimmer.</summary>
    Generating,
    /// <summary>Rendu progressif : WebView visible + bannière de streaming.</summary>
    Streaming,
    /// <summary>Document finalisé et interactif.</summary>
    Ready
}

/// <summary>
/// Hôte d'aperçu du document : état vide brandé, squelette shimmer, rendu progressif,
/// barre d'outils flottante auto-masquable (styles, zoom, exports, lecture à voix haute).
/// </summary>
public sealed partial class PreviewHost : UserControl
{
    // ------------------------------------------------------------------
    // DependencyProperties (compatibles AOT / WinRT)
    // ------------------------------------------------------------------

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(PreviewState), typeof(PreviewHost),
            new PropertyMetadata(PreviewState.Empty, OnStateChanged));

    /// <summary>État visuel courant de l'aperçu.</summary>
    public PreviewState State
    {
        get => (PreviewState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(PreviewHost),
            new PropertyMetadata("Préparation de l'aperçu…"));

    /// <summary>Message d'état affiché sur le squelette et la bannière de streaming (LiveRegion).</summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public static readonly DependencyProperty StreamingProgressProperty =
        DependencyProperty.Register(nameof(StreamingProgress), typeof(double), typeof(PreviewHost),
            new PropertyMetadata(0d, OnStreamingProgressChanged));

    /// <summary>Progression du rendu en direct (0–100). 0 = indéterminé.</summary>
    public double StreamingProgress
    {
        get => (double)GetValue(StreamingProgressProperty);
        set => SetValue(StreamingProgressProperty, value);
    }

    // ------------------------------------------------------------------
    // Événements publics
    // ------------------------------------------------------------------

    public event EventHandler? ExportPdfRequested;
    public event EventHandler? ExportWordRequested;
    public event EventHandler? ExportRtfRequested;
    /// <summary>Si aucun abonné, une copie du texte du document est effectuée par défaut.</summary>
    public event EventHandler? CopyRequested;
    /// <summary>Si aucun abonné, la boîte de dialogue d'impression WebView2 est affichée.</summary>
    public event EventHandler? PrintRequested;
    /// <summary>Si aucun abonné, une lecture via speechSynthesis du WebView est tentée.</summary>
    public event EventHandler? ReadAloudRequested;
    /// <summary>Déclenché quand l'utilisateur change de style visuel (Tag du preset).</summary>
    public event EventHandler<string>? StylePresetChanged;

    // ------------------------------------------------------------------
    // Champs privés
    // ------------------------------------------------------------------

    private readonly DispatcherTimer _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
    private double _zoomFactor = 1.0;

    private const double ZoomStep = 0.1;
    private const double ZoomMin = 0.5;
    private const double ZoomMax = 2.0;

    private bool IsToolbarContext => State == PreviewState.Streaming || State == PreviewState.Ready;

    public PreviewHost()
    {
        InitializeComponent();

        _idleTimer.Tick += OnIdleTimerTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        UpdateVisualState();
    }

    // ------------------------------------------------------------------
    // Cycle de vie
    // ------------------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateVisualState();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _idleTimer.Stop();
        ShimmerStoryboard.Stop();
        PulseStoryboard.Stop();
        EmptyEntranceStoryboard.Stop();
    }

    // ------------------------------------------------------------------
    // Machine à états visuelle
    // ------------------------------------------------------------------

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PreviewHost)d).UpdateVisualState();

    private static void OnStreamingProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (PreviewHost)d;
        var value = (double)e.NewValue;
        self.StreamingProgressBar.IsIndeterminate = value <= 0;
        self.StreamingPercentText.Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateVisualState()
    {
        var state = State;

        EmptyStatePanel.Visibility = state == PreviewState.Empty ? Visibility.Visible : Visibility.Collapsed;
        SkeletonOverlay.Visibility = state == PreviewState.Generating ? Visibility.Visible : Visibility.Collapsed;
        WebViewControl.Visibility = IsToolbarContext ? Visibility.Visible : Visibility.Collapsed;
        StreamingBanner.Visibility = state == PreviewState.Streaming ? Visibility.Visible : Visibility.Collapsed;

        if (!IsLoaded)
        {
            return;
        }

        if (state == PreviewState.Generating) ShimmerStoryboard.Begin(); else ShimmerStoryboard.Stop();
        if (state == PreviewState.Streaming) PulseStoryboard.Begin(); else PulseStoryboard.Stop();
        if (state == PreviewState.Empty) EmptyEntranceStoryboard.Begin();

        if (IsToolbarContext)
        {
            ShowToolbar();
            RestartIdleTimer();
        }
        else
        {
            HideToolbar();
        }
    }

    // ------------------------------------------------------------------
    // Barre d'outils auto-masquable (3 s d'inactivité)
    // ------------------------------------------------------------------

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!IsToolbarContext)
        {
            return;
        }

        ShowToolbar();
        RestartIdleTimer();
    }

    private void ShowToolbar()
    {
        if (!IsToolbarContext)
        {
            return;
        }

        FloatingToolbar.IsHitTestVisible = true;
        VisualStateManager.GoToState(this, "ToolbarVisible", true);
    }

    private void HideToolbar()
    {
        VisualStateManager.GoToState(this, "ToolbarHidden", true);
        FloatingToolbar.IsHitTestVisible = false;
    }

    private void RestartIdleTimer()
    {
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void OnIdleTimerTick(object? sender, object e)
    {
        _idleTimer.Stop();
        HideToolbar();
    }

    private void OnToolbarPointerEntered(object sender, PointerRoutedEventArgs e) => _idleTimer.Stop();

    private void OnToolbarPointerExited(object sender, PointerRoutedEventArgs e) => RestartIdleTimer();

    private void OnPresetDropDownOpened(object sender, object e) => _idleTimer.Stop();

    private void OnPresetDropDownClosed(object sender, object e) => RestartIdleTimer();

    // ------------------------------------------------------------------
    // Zoom (50 % – 200 %)
    // ------------------------------------------------------------------

    private async void OnZoomInClicked(object sender, RoutedEventArgs e) => await SetZoomAsync(_zoomFactor + ZoomStep);

    private async void OnZoomOutClicked(object sender, RoutedEventArgs e) => await SetZoomAsync(_zoomFactor - ZoomStep);

    private async void OnZoomResetClicked(object sender, RoutedEventArgs e) => await SetZoomAsync(1.0);

    /// <summary>Réinitialise le zoom à 100 %.</summary>
    public Task ResetZoomAsync() => SetZoomAsync(1.0);

    private async Task SetZoomAsync(double value)
    {
        _zoomFactor = Math.Clamp(value, ZoomMin, ZoomMax);
        ZoomPercentText.Text = string.Format(CultureInfo.InvariantCulture, "{0} %", (int)Math.Round(_zoomFactor * 100));
        RestartIdleTimer();

        try
        {
            if (WebViewControl.CoreWebView2 != null)
            {
                var zoom = _zoomFactor.ToString(CultureInfo.InvariantCulture);
                await WebViewControl.ExecuteScriptAsync($"document.documentElement.style.zoom='{zoom}';");
            }
        }
        catch
        {
            // Le WebView n'est pas encore initialisé : le zoom sera appliqué au prochain rendu.
        }
    }

    // ------------------------------------------------------------------
    // Sélecteur de style visuel
    // ------------------------------------------------------------------

    private async void OnStylePresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StylePresetComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var preset = item.Tag as string ?? "classique";
        RestartIdleTimer();
        StylePresetChanged?.Invoke(this, preset);

        try
        {
            if (WebViewControl.CoreWebView2 != null)
            {
                await WebViewControl.ExecuteScriptAsync(
                    $"document.documentElement.setAttribute('data-style-preset','{preset}');");
            }
        }
        catch
        {
            // Le document ne gère pas (encore) les presets : l'événement permet au parent de régénérer.
        }
    }

    // ------------------------------------------------------------------
    // Exports / Presse-papiers / Impression / Lecture
    // ------------------------------------------------------------------

    private void OnExportPdfClicked(object sender, RoutedEventArgs e)
    {
        RestartIdleTimer();
        ExportPdfRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExportWordClicked(object sender, RoutedEventArgs e)
    {
        RestartIdleTimer();
        ExportWordRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExportRtfClicked(object sender, RoutedEventArgs e)
    {
        RestartIdleTimer();
        ExportRtfRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        RestartIdleTimer();

        if (CopyRequested != null)
        {
            CopyRequested(this, EventArgs.Empty);
        }
        else
        {
            await CopyDocumentToClipboardAsync();
        }
    }

    private void OnPrintClicked(object sender, RoutedEventArgs e)
    {
        RestartIdleTimer();

        if (PrintRequested != null)
        {
            PrintRequested(this, EventArgs.Empty);
        }
        else
        {
            try
            {
                WebViewControl.CoreWebView2.ShowPrintUI(Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser);
            }
            catch
            {
                // WebView non prêt.
            }
        }
    }

    private async void OnReadAloudClicked(object sender, RoutedEventArgs e)
    {
        RestartIdleTimer();

        if (ReadAloudRequested != null)
        {
            ReadAloudRequested(this, EventArgs.Empty);
        }
        else
        {
            await ReadAloudFallbackAsync();
        }
    }

    // ------------------------------------------------------------------
    // API publique WebView
    // ------------------------------------------------------------------

    /// <summary>Garantit l'initialisation du contrôle WebView2.</summary>
    public async Task EnsureWebViewReadyAsync()
    {
        if (WebViewControl.CoreWebView2 == null)
        {
            await WebViewControl.EnsureCoreWebView2Async();
        }
    }

    /// <summary>Charge un document HTML dans l'aperçu et passe à l'état <see cref="PreviewState.Ready"/>.</summary>
    public async Task NavigateToStringAsync(string html)
    {
        await EnsureWebViewReadyAsync();
        WebViewControl.NavigateToString(html ?? string.Empty);
        State = PreviewState.Ready;
    }

    /// <summary>Accès avancé au WebView2 sous-jacent (impression, scripts, etc.).</summary>
    public WebView2 PreviewWebView => WebViewControl;

    // ------------------------------------------------------------------
    // Solutions de repli autonomes
    // ------------------------------------------------------------------

    private async Task CopyDocumentToClipboardAsync()
    {
        try
        {
            await EnsureWebViewReadyAsync();
            var json = await WebViewControl.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
            var text = JsonSerializer.Deserialize<string>(json);

            if (!string.IsNullOrWhiteSpace(text))
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
            }
        }
        catch
        {
            // Presse-papiers ou WebView indisponible.
        }
    }

    private async Task ReadAloudFallbackAsync()
    {
        try
        {
            await EnsureWebViewReadyAsync();
            await WebViewControl.ExecuteScriptAsync(
                "(function(){var t=document.body?document.body.innerText:'';" +
                "if(t){var u=new SpeechSynthesisUtterance(t);u.lang='fr-FR';" +
                "speechSynthesis.cancel();speechSynthesis.speak(u);}})()");
        }
        catch
        {
            // Synthèse vocale indisponible dans le contexte WebView.
        }
    }
}