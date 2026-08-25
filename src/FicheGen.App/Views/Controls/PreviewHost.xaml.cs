using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using FicheGen.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;
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
    Ready,
    /// <summary>Composant WebView2 manquant ou erreur d'initialisation (UX-04).</summary>
    Error
}

/// <summary>
/// Hôte d'aperçu du document : état vide brandé, squelette shimmer, rendu progressif,
/// barre d'outils permanente (styles, zoom, exports, lecture à voix haute) et gestion d'erreur WebView2.
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

    // Le zoom a une SEULE source de vérité : ResultViewModel.ZoomFactor (persisté).
    // Ce champ n'est plus qu'un cache de la dernière valeur appliquée au rendu.
    private double _lastAppliedZoom = 1.0;
    private ResultViewModel? _currentViewModel;
    private bool _isViewModelWired;
    private bool _isWebView2Initialized;
    private bool _isWebView2Failed;
    private bool _isSpeaking;

    private const double ZoomStep = 0.1;

    public PreviewHost()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;

        UpdateVisualState();
    }

    // ------------------------------------------------------------------
    // Cycle de vie & Synchronisation ViewModel
    // ------------------------------------------------------------------

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is ResultViewModel vm)
        {
            WireViewModel(vm);
            SyncFromViewModel(vm);
            // Applique immédiatement le zoom restauré par le ViewModel (source de vérité).
            _ = SetZoomAsync(vm.ZoomFactor);
        }
        else
        {
            UnwireViewModel();
            _currentViewModel = null;
            if (State != PreviewState.Error)
            {
                State = PreviewState.Empty;
            }
        }
    }

    /// <summary>
    /// Abonne <see cref="OnViewModelPropertyChanged"/> ET <see cref="OnViewModelPrintRequested"/>.
    /// Idempotent, et rejoué après chaque rechargement du contrôle : sans cela,
    /// l'abonnement d'impression ne survit pas à un cycle Loaded/Unloaded.
    /// </summary>
    private void WireViewModel(ResultViewModel vm)
    {
        if (_isViewModelWired && ReferenceEquals(_currentViewModel, vm))
        {
            return;
        }
        UnwireViewModel();
        _currentViewModel = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.PrintRequested += OnViewModelPrintRequested;
        _isViewModelWired = true;
        SyncPresetComboBox(vm.ActivePresetId);
    }

    private void UnwireViewModel()
    {
        if (_currentViewModel == null || !_isViewModelWired)
        {
            return;
        }
        _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _currentViewModel.PrintRequested -= OnViewModelPrintRequested;
        _isViewModelWired = false;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is ResultViewModel vm)
        {
            if (e.PropertyName == nameof(ResultViewModel.CurrentHtml) ||
                e.PropertyName == nameof(ResultViewModel.PreviewHtml) ||
                e.PropertyName == nameof(ResultViewModel.CurrentDocument))
            {
                SyncHtmlFromViewModel(vm);
            }
            else if (e.PropertyName == nameof(ResultViewModel.IsBusy))
            {
                if (vm.IsBusy && string.IsNullOrWhiteSpace(vm.CurrentHtml))
                {
                    StatusText = string.IsNullOrWhiteSpace(vm.StatusMessage) ? Services.L10n.Get("Preview_Generating") : vm.StatusMessage;
                    State = PreviewState.Generating;
                }
                else if (!vm.IsBusy && !string.IsNullOrWhiteSpace(vm.CurrentHtml))
                {
                    State = PreviewState.Ready;
                }
                else if (!vm.IsBusy && string.IsNullOrWhiteSpace(vm.CurrentHtml))
                {
                    State = PreviewState.Empty;
                }
            }
            else if (e.PropertyName == nameof(ResultViewModel.StatusMessage))
            {
                if (!string.IsNullOrWhiteSpace(vm.StatusMessage))
                {
                    StatusText = vm.StatusMessage;
                }
            }
            else if (e.PropertyName == nameof(ResultViewModel.ActivePresetId))
            {
                SyncPresetComboBox(vm.ActivePresetId);
            }
            else if (e.PropertyName == nameof(ResultViewModel.ZoomFactor))
            {
                _ = SetZoomAsync(vm.ZoomFactor);
            }
        }
    }

    private void SyncFromViewModel(ResultViewModel vm)
    {
        if (vm.IsBusy && string.IsNullOrWhiteSpace(vm.CurrentHtml))
        {
            StatusText = string.IsNullOrWhiteSpace(vm.StatusMessage) ? Services.L10n.Get("Preview_Generating") : vm.StatusMessage;
            State = PreviewState.Generating;
        }
        else if (!string.IsNullOrWhiteSpace(vm.CurrentHtml))
        {
            SyncHtmlFromViewModel(vm);
        }
        else
        {
            State = PreviewState.Empty;
        }
    }

    private async void SyncHtmlFromViewModel(ResultViewModel vm)
    {
        if (!string.IsNullOrWhiteSpace(vm.CurrentHtml))
        {
            await NavigateToStringAsync(vm.CurrentHtml);
        }
        else
        {
            State = PreviewState.Empty;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(version))
            {
                _isWebView2Failed = true;
                State = PreviewState.Error;
            }
        }
        catch
        {
            _isWebView2Failed = true;
            State = PreviewState.Error;
        }

        UpdateVisualState();
        if (WebViewControl != null && !_isWebView2Failed)
        {
            WebViewControl.CoreProcessFailed -= OnCoreProcessFailed;
            WebViewControl.CoreProcessFailed += OnCoreProcessFailed;
        }

        // Rejoue l'abonnement si le contrôle a été déchargé puis rechargé
        // sans changement de DataContext (le flag rend l'opération idempotente).
        if (_currentViewModel != null)
        {
            WireViewModel(_currentViewModel);
            SyncFromViewModel(_currentViewModel);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (WebViewControl != null)
        {
            WebViewControl.CoreProcessFailed -= OnCoreProcessFailed;
        }
        ShimmerStoryboard?.Stop();
        PulseStoryboard?.Stop();
        EmptyEntranceStoryboard?.Stop();

        if (_currentViewModel != null)
        {
            UnwireViewModel();
        }
    }

    private void OnCoreProcessFailed(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedEventArgs args)
    {
        Log.Warning("WebView2 process failed: {Reason}", args.ProcessFailedKind);
        _isWebView2Initialized = false;
        State = PreviewState.Error;
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
        if (self.StreamingProgressBar != null)
        {
            self.StreamingProgressBar.IsIndeterminate = value <= 0;
        }
        if (self.StreamingPercentText != null)
        {
            self.StreamingPercentText.Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateVisualState()
    {
        var state = State;

        if (EmptyStatePanel != null)
            EmptyStatePanel.Visibility = state == PreviewState.Empty ? Visibility.Visible : Visibility.Collapsed;
        if (SkeletonOverlay != null)
            SkeletonOverlay.Visibility = state == PreviewState.Generating ? Visibility.Visible : Visibility.Collapsed;
        if (WebViewControl != null)
            WebViewControl.Visibility = (state == PreviewState.Streaming || state == PreviewState.Ready) ? Visibility.Visible : Visibility.Collapsed;
        if (StreamingBanner != null)
            StreamingBanner.Visibility = state == PreviewState.Streaming ? Visibility.Visible : Visibility.Collapsed;
        if (WebView2ErrorPanel != null)
            WebView2ErrorPanel.Visibility = state == PreviewState.Error ? Visibility.Visible : Visibility.Collapsed;

        // Activation / désactivation des contrôles de la barre d'outils
        var hasDoc = state == PreviewState.Streaming || state == PreviewState.Ready;
        if (ExportWordButton != null) ExportWordButton.IsEnabled = hasDoc;
        if (ExportPdfButton != null) ExportPdfButton.IsEnabled = hasDoc;
        if (PrintButton != null) PrintButton.IsEnabled = hasDoc;
        if (StudentViewButton != null) StudentViewButton.IsEnabled = hasDoc;
        if (StylePresetComboBox != null) StylePresetComboBox.IsEnabled = hasDoc;

        if (!IsLoaded)
        {
            return;
        }

        if (state == PreviewState.Generating) ShimmerStoryboard?.Begin(); else ShimmerStoryboard?.Stop();
        if (state == PreviewState.Streaming) PulseStoryboard?.Begin(); else PulseStoryboard?.Stop();
        if (state == PreviewState.Empty) EmptyEntranceStoryboard?.Begin();
    }

    // ------------------------------------------------------------------
    // Zoom (source de vérité : ResultViewModel.ZoomFactor, 50 % – 200 %)
    // ------------------------------------------------------------------

    private void OnZoomInClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultViewModel vm) vm.ZoomInCommand.Execute(null);
    }

    private void OnZoomOutClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultViewModel vm) vm.ZoomOutCommand.Execute(null);
    }

    private void OnZoomResetClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultViewModel vm) vm.ResetZoomCommand.Execute(null);
    }

    /// <summary>Réinitialise le zoom à 100 % via la source de vérité (VM).</summary>
    public Task ResetZoomAsync()
    {
        if (DataContext is ResultViewModel vm) vm.ResetZoomCommand.Execute(null);
        return Task.CompletedTask;
    }

    /// <summary>Côté rendu uniquement : applique visuellement le zoom demandé.
    /// Appelé par la notification <c>ZoomFactor</c> du ViewModel — jamais par les boutons.</summary>
    private async Task SetZoomAsync(double value)
    {
        _lastAppliedZoom = value;
        if (ZoomPercentText != null)
        {
            ZoomPercentText.Text = string.Format(CultureInfo.InvariantCulture, "{0} %", (int)Math.Round(value * 100));
        }

        try
        {
            if (WebViewControl?.CoreWebView2 != null)
            {
                var zoom = value.ToString(CultureInfo.InvariantCulture);
                await WebViewControl.ExecuteScriptAsync($"document.documentElement.style.zoom='{zoom}';");
            }
        }
        catch
        {
            // Le WebView n'est pas encore initialisé
        }
    }

    // ------------------------------------------------------------------
    // Sélecteur de style visuel
    // ------------------------------------------------------------------

    /// <summary>Aligne la liste déroulante sur le préréglage actif du ViewModel
    /// (document chargé depuis l'historique, préférence persistée…).</summary>
    private void SyncPresetComboBox(string? presetId)
    {
        if (StylePresetComboBox is null || string.IsNullOrWhiteSpace(presetId))
        {
            return;
        }

        for (var i = 0; i < StylePresetComboBox.Items.Count; i++)
        {
            if (StylePresetComboBox.Items[i] is ComboBoxItem { Tag: string tag } &&
                string.Equals(tag, presetId, StringComparison.Ordinal))
            {
                if (StylePresetComboBox.SelectedIndex != i)
                {
                    _isSyncingPresetCombo = true;
                    StylePresetComboBox.SelectedIndex = i;
                    _isSyncingPresetCombo = false;
                }
                return;
            }
        }
    }

    private bool _isSyncingPresetCombo;

    private async void OnStylePresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingPresetCombo)
        {
            return;
        }

        if (StylePresetComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var preset = item.Tag as string ?? "classique";
        StylePresetChanged?.Invoke(this, preset);

        if (DataContext is ResultViewModel vm)
        {
            vm.ChangePreset(preset);
        }

        try
        {
            if (WebViewControl?.CoreWebView2 != null)
            {
                await WebViewControl.ExecuteScriptAsync(
                    $"document.documentElement.setAttribute('data-style-preset','{preset}');");
            }
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------
    // Exports / Presse-papiers / Impression / Lecture / Version Élève
    // ------------------------------------------------------------------

    private void OnStudentViewClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultViewModel vm)
        {
            vm.ToggleStudentView();
        }
    }

    private async void OnExportPdfClicked(object sender, RoutedEventArgs e)
    {
        if (ExportPdfRequested != null)
        {
            ExportPdfRequested(this, EventArgs.Empty);
        }
        else if (DataContext is ResultViewModel vm)
        {
            await vm.ExportPdfAsync();
        }
    }

    private async void OnExportWordClicked(object sender, RoutedEventArgs e)
    {
        if (ExportWordRequested != null)
        {
            ExportWordRequested(this, EventArgs.Empty);
        }
        else if (DataContext is ResultViewModel vm)
        {
            await vm.ExportDocxAsync();
        }
    }

    private async void OnExportRtfClicked(object sender, RoutedEventArgs e)
    {
        if (ExportRtfRequested != null)
        {
            ExportRtfRequested(this, EventArgs.Empty);
        }
        else if (DataContext is ResultViewModel vm)
        {
            await vm.ExportRtfAsync();
        }
    }

    private async void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        if (CopyRequested != null)
        {
            CopyRequested(this, EventArgs.Empty);
        }
        else if (DataContext is ResultViewModel vm)
        {
            vm.CopyPlainText();
            await CopyDocumentToClipboardAsync();
        }
        else
        {
            await CopyDocumentToClipboardAsync();
        }
    }

    private async void OnPrintClicked(object sender, RoutedEventArgs e)
    {
        // Un hôte qui a souscrit à l'événement sortant prend le relais ;
        // sinon l'aperçu affiche lui-même la boîte de dialogue d'impression.
        if (PrintRequested != null)
        {
            PrintRequested(this, EventArgs.Empty);
            return;
        }

        await ShowPrintDialogAsync();
    }

    private void OnViewModelPrintRequested(object? sender, PrintRequestedEventArgs e)
        => _ = ShowPrintDialogAsync();

    /// <summary>Affiche la boîte de dialogue d'impression WebView2 sur l'aperçu visible.
    /// Point d'entrée unique pour le bouton outil et Ctrl+P depuis les pages hôtes.</summary>
    private async Task ShowPrintDialogAsync()
    {
        try
        {
            var ready = await EnsureWebViewReadyAsync();
            if (ready && WebViewControl?.CoreWebView2 != null)
            {
                WebViewControl.CoreWebView2.ShowPrintUI(
                    Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Impression WebView2 impossible.");
        }
    }

    private async void OnReadAloudClicked(object sender, RoutedEventArgs e)
    {
        if (ReadAloudRequested != null)
        {
            ReadAloudRequested(this, EventArgs.Empty);
        }
        else
        {
            await ReadAloudFallbackAsync();
        }
    }

    private void OnFindClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultViewModel vm && vm.ToggleFindBarCommand.CanExecute(null))
        {
            vm.ToggleFindBarCommand.Execute(null);
        }
    }

    private void OnOpenExportFolderClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResultViewModel vm && vm.OpenLastExportedFolderCommand.CanExecute(null))
        {
            vm.OpenLastExportedFolderCommand.Execute(null);
        }
    }

    // ------------------------------------------------------------------
    // État vide : raccourcis vers le shell
    // ------------------------------------------------------------------

    private void OnPaletteClicked(object sender, RoutedEventArgs e)
        => (App.CurrentMainWindow as MainWindow)?.OpenCommandPalette();

    private void OnOpenHistoryClicked(object sender, RoutedEventArgs e)
        => (App.CurrentMainWindow as MainWindow)?.NavigateToTag("HistoryPage");

    // ------------------------------------------------------------------
    // Zoom molette (Ctrl + molette) et animation connectée
    // ------------------------------------------------------------------

    private void OnPreviewWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if ((e.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) == 0) return;

        var delta = e.GetCurrentPoint(WebViewControl).Properties.MouseWheelDelta;
        // Passe par la source de vérité (VM) : clamp + persistance + notification.
        if (DataContext is ResultViewModel vm)
        {
            vm.SetZoom(vm.ZoomFactor + (delta > 0 ? ZoomStep : -ZoomStep));
        }
        e.Handled = true;
    }

    /// <summary>
    /// Joue l'animation connectée « carte d'historique → aperçu » préparée par HistoryPage.
    /// Silencieux si aucune animation n'est en attente ou si les animations sont désactivées.
    /// </summary>
    public async Task RunIncomingAnimationAsync(Frame? frame)
    {
        try
        {
            if (!Services.UiMotion.Enabled || frame == null) return;

            var anim = Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService
                .GetForCurrentView().GetAnimation("openDoc");
            if (anim == null) return;

            if (ContentHost.ActualWidth == 0)
            {
                var loaded = new TaskCompletionSource<bool>();
                RoutedEventHandler? handler = null;
                handler = (_, _) => { ContentHost.Loaded -= handler; loaded.TrySetResult(true); };
                ContentHost.Loaded += handler;
                await Task.WhenAny(loaded.Task, Task.Delay(800));
            }

            UpdateLayout();
            anim.TryStart(ContentHost);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Animation connectée non jouée.");
        }
    }

    // ------------------------------------------------------------------
    // Actions WebView2 Manquant (UX-04)
    // ------------------------------------------------------------------

    private async void OnInstallWebView2Clicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://go.microsoft.com/fwlink/p/?LinkId=2124703"));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Impossible d'ouvrir le lien de téléchargement WebView2.");
        }
    }

    private void OnCopyWebView2LinkClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            Clipboard.SetContent(dataPackage);
            if (WebView2LinkCopiedNotice != null) WebView2LinkCopiedNotice.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Impossible de copier le lien dans le presse-papiers.");
        }
    }

    // ------------------------------------------------------------------
    // Initialisation & Navigation WebView2
    // ------------------------------------------------------------------

    private async Task<bool> EnsureWebViewReadyAsync()
    {
        if (_isWebView2Initialized) return true;
        if (_isWebView2Failed || WebViewControl == null) return false;

        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(version))
            {
                _isWebView2Failed = true;
                State = PreviewState.Error;
                return false;
            }

            await WebViewControl.EnsureCoreWebView2Async();
            _isWebView2Initialized = true;
            _isWebView2Failed = false;

            // Désactive le zoom natif du navigateur (Ctrl+molette WebView2) :
            // seul le pipeline ResultViewModel.ZoomFactor doit zoomer.
            WebViewControl.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // Ré-applique le zoom courant après chaque navigation : le premier
            // chargement HTML réinitialise sinon le style visuel à 100 %.
            WebViewControl.NavigationCompleted -= OnWebViewNavigationCompleted;
            WebViewControl.NavigationCompleted += OnWebViewNavigationCompleted;

            return true;
        }
        catch (Exception ex)
        {
            _isWebView2Failed = true;
            State = PreviewState.Error;
            Log.Error(ex, "Échec de l'initialisation de WebView2.");
            return false;
        }
    }

    /// <summary>Réinitialise l'état d'échec et retente l'initialisation de WebView2.</summary>
    public async Task<bool> RetryWebView2Async()
    {
        _isWebView2Failed = false;
        _isWebView2Initialized = false;
        return await EnsureWebViewReadyAsync();
    }

    private void OnWebViewNavigationCompleted(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        // Après une navigation (nouveau document), ré-applique le zoom du ViewModel.
        if (DataContext is ResultViewModel vm)
        {
            _ = SetZoomAsync(vm.ZoomFactor);
        }
    }

    /// <summary>Charge un document HTML dans l'aperçu et passe à l'état <see cref="PreviewState.Ready"/>.</summary>
    public async Task NavigateToStringAsync(string html)
    {
        try
        {
            var ready = await EnsureWebViewReadyAsync();
            if (!ready || WebViewControl == null)
            {
                State = PreviewState.Error;
                return;
            }

            WebViewControl.NavigateToString(html ?? string.Empty);
            State = PreviewState.Ready;
        }
        catch (Exception ex)
        {
            State = PreviewState.Error;
            Log.Error(ex, "Erreur lors du rendu HTML dans WebView2.");
        }
    }

    /// <summary>Accès avancé au WebView2 sous-jacent (impression, scripts, etc.).</summary>
    public WebView2? PreviewWebView => WebViewControl;

    // ------------------------------------------------------------------
    // Solutions de repli autonomes
    // ------------------------------------------------------------------

    private async Task CopyDocumentToClipboardAsync()
    {
        try
        {
            var ready = await EnsureWebViewReadyAsync();
            if (!ready || WebViewControl == null) return;

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
            var ready = await EnsureWebViewReadyAsync();
            if (!ready || WebViewControl == null) return;

            if (_isSpeaking)
            {
                await WebViewControl.ExecuteScriptAsync("window.speechSynthesis && window.speechSynthesis.cancel();");
                _isSpeaking = false;
                if (ReadAloudMenuItem != null) ReadAloudMenuItem.Text = Services.L10n.Get("PH_MenuReadAloud.Text");
                if (ReadAloudMenuIcon != null) ReadAloudMenuIcon.Glyph = "\uE995";
            }
            else
            {
                _isSpeaking = true;
                if (ReadAloudMenuItem != null) ReadAloudMenuItem.Text = Services.L10n.Get("PH_ReadAloudStop");
                if (ReadAloudMenuIcon != null) ReadAloudMenuIcon.Glyph = "\uE71A";

                await WebViewControl.ExecuteScriptAsync(
                    "(function(){" +
                    "  var t = document.body ? document.body.innerText : '';" +
                    "  if(t && window.speechSynthesis){" +
                    "    var u = new SpeechSynthesisUtterance(t);" +
                    "    u.lang = 'fr-FR';" +
                    "    u.onend = function() { window.speechSynthesis.cancel(); };" +
                    "    window.speechSynthesis.cancel();" +
                    "    window.speechSynthesis.speak(u);" +
                    "  }" +
                    "})()");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Synthèse vocale indisponible dans le contexte WebView.");
            _isSpeaking = false;
        }
    }
}