using System;
using System.Linq;
using System.Threading.Tasks;
using FicheGen.App.Services;
using FicheGen.App.ViewModels;
using FicheGen.App.Views;
using FicheGen.App.Views.Controls;
using FicheGen.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Serilog;

namespace FicheGen.App;

public sealed partial class MainWindow
{
    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateTo(typeof(SettingsPage), "SettingsPage");
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString() ?? "FichePage";
            Type pageType = tag switch
            {
                "FichePage" => typeof(FichePage),
                "EvaluationPage" => typeof(EvaluationPage),
                "QuizPage" => typeof(QuizPage),
                "HistoryPage" => typeof(HistoryPage),
                "SettingsPage" => typeof(SettingsPage),
                _ => typeof(FichePage)
            };

            NavigateTo(pageType, tag);
        }
    }

    private void NavigateTo(Type pageType, string tag)
    {
        if (ContentFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());

        if (ContentFrame.Content is Page page)
        {
            page.DataContext = tag switch
            {
                "FichePage" => App.Services.GetRequiredService<FicheFormViewModel>(),
                "EvaluationPage" => App.Services.GetRequiredService<EvaluationViewModel>(),
                "QuizPage" => App.Services.GetRequiredService<QuizViewModel>(),
                "HistoryPage" => App.Services.GetRequiredService<HistoryViewModel>(),
                "SettingsPage" => App.Services.GetRequiredService<SettingsViewModel>(),
                _ => App.Services.GetRequiredService<FicheFormViewModel>()
            };

            // Ré-applique la visibilité du volet Assistant sur la nouvelle page
            ToggleAssistantVisibility(AssistantToggleButton.IsChecked == true);
        }

        StatusSectionText.Text = tag switch
        {
            "FichePage" => Services.L10n.Get("Section_Fiche"),
            "EvaluationPage" => Services.L10n.Get("Section_Evaluation"),
            "QuizPage" => Services.L10n.Get("Section_Quiz"),
            "HistoryPage" => Services.L10n.Get("Section_History"),
            "SettingsPage" => Services.L10n.Get("Section_Settings"),
            _ => Services.L10n.Get("Section_Fiche")
        };

        _shellState.LastNavigationTag = tag;

        // En revenant des Paramètres, on rafraîchit la puce IA et le profil
        if (_wasOnSettings)
        {
            RefreshAiChip();
            RefreshTeacherBadge();
            _wasOnSettings = false;
        }
        if (tag == "SettingsPage")
        {
            _wasOnSettings = true;
        }

        SaveShellState();
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // L'entrée « Aide » ne change pas la sélection (SelectsOnInvoked = False)
        if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag } && tag == "Help")
        {
            ShowShortcutsDialog();
        }
    }

    private NavigationViewItem? FindNavItem(string? tag) =>
        NavView.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.Ordinal));

    public void NavigateToTag(string tag)
    {
        if (tag == "SettingsPage")
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        var item = FindNavItem(tag);
        if (item is not null)
        {
            NavView.SelectedItem = item;
        }
    }

    private void OnGlobalSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText;

        void ApplyToHistory(HistoryPage hp)
        {
            hp.SetSearchQuery(query);
            hp.FocusSearchBox();
        }

        if (ContentFrame.Content is HistoryPage current)
        {
            ApplyToHistory(current);
        }
        else
        {
            void handler(object? s, NavigationEventArgs e)
            {
                ContentFrame.Navigated -= handler;
                if (ContentFrame.Content is HistoryPage hp) ApplyToHistory(hp);
            }
            ContentFrame.Navigated += handler;
            NavigateToTag("HistoryPage");
        }

        SetStatus(Services.L10n.Get("Status_SearchHistory"));
        GlobalSearchBox.Text = string.Empty;
    }

    // ==========================================================================
    //  Volet Assistant IA
    // ==========================================================================
    private void OnAssistantToggleClicked(object sender, RoutedEventArgs e)
    {
        SetAssistantVisible(AssistantToggleButton.IsChecked == true);
    }

    private void SetAssistantVisible(bool visible)
    {
        ToggleAssistantVisibility(visible);
        _shellState.IsAssistantVisible = visible;
        SaveShellState();
    }

    private void ToggleAssistantVisibility(bool isVisible)
    {
        if (ContentFrame.Content is IAssistantHostPage hostPage)
        {
            hostPage.SetAssistantVisible(isVisible);
        }
    }

    private void OnNewFicheClicked(object sender, RoutedEventArgs e) => CreateNewFiche();

    private void OnSearchClicked(object sender, RoutedEventArgs e) => FocusHistorySearch();

    private void OnShortcutsClicked(object sender, RoutedEventArgs e) => ShowShortcutsDialog();

    private void OnAccountClicked(object sender, RoutedEventArgs e)
    {
        ProfileButton.Flyout?.Hide();
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(100);
            var dlg = new FicheGen.App.Views.Controls.AccountDialog
            {
                XamlRoot = Content.XamlRoot
            };
            await dlg.ShowAsync();
            RefreshTeacherBadge();
        });
    }

    private void OnProfileSettingsClicked(object sender, RoutedEventArgs e)
    {
        ProfileButton.Flyout?.Hide();
        NavigateToTag("SettingsPage");
    }

    private void OnProfileShortcutsClicked(object sender, RoutedEventArgs e)
    {
        ProfileButton.Flyout?.Hide();
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(150);
            ShowShortcutsDialog();
        });
    }

    private void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        ProfileButton.Flyout?.Hide();
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(150);
            ShowAboutDialog();
        });
    }

    private void CreateNewFiche()
    {
        NavigateToTag("FichePage");
        SetStatus(Services.L10n.Get("Status_NewFiche"));

        if (ContentFrame.Content is Page { DataContext: object dc } &&
            ExecuteMatchingCommand(dc, out _, "ResetFormCommand", "NewFicheCommand"))
        {
            ShowHint(Services.L10n.Get("Hint_NewFiche_Title"), Services.L10n.Get("Hint_NewFiche_Message"));
        }
    }

    private void FocusHistorySearch()
    {
        if (ContentFrame.Content is HistoryPage hp)
        {
            hp.FocusSearchBox();
            SetStatus(Services.L10n.Get("Status_SearchHistory"));
            return;
        }

        void handler(object? s, NavigationEventArgs e)
        {
            ContentFrame.Navigated -= handler;
            if (ContentFrame.Content is HistoryPage h) h.FocusSearchBox();
        }
        ContentFrame.Navigated += handler;
        NavigateToTag("HistoryPage");
        SetStatus(Services.L10n.Get("Status_SearchHistory"));
    }

    // ==========================================================================
    //  Thème (Clair / Sombre / Sombre OLED / Système)
    // ==========================================================================
    private void OnThemeItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem item && item.Tag is string theme)
        {
            ApplyTheme(theme);
        }
    }

    private void ApplyTheme(string theme, bool persist = true)
    {
        _shellState.Theme = theme is "Light" or "Dark" or "Oled" or "System" ? theme : "System";
        bool isOled = _shellState.Theme == "Oled";

        var elementTheme = _shellState.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" or "Oled" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (this.Content is FrameworkElement rootElem)
        {
            rootElem.RequestedTheme = elementTheme;
        }
        RootGrid.RequestedTheme = elementTheme;
        ContentFrame.RequestedTheme = elementTheme;
        if (ContentFrame.Content is FrameworkElement pageElem)
        {
            pageElem.RequestedTheme = elementTheme;
        }

        if (isOled)
        {
            try
            {
                SystemBackdrop = null;
            }
            catch { }

            RootGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
            ContentFrame.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));

            // Surcharges de ressources pour un thème OLED noir pur absolu (#000000)
            RootGrid.Resources["ApplicationPageBackgroundThemeBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
            RootGrid.Resources["SolidBackgroundFillColorBaseBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
            RootGrid.Resources["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 13, 13, 13));
            RootGrid.Resources["CardBackgroundFillColorSecondaryBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20));
            RootGrid.Resources["CardStrokeColorDefaultBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 38, 38));
            RootGrid.Resources["DividerStrokeColorDefaultBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30));
            RootGrid.Resources["LayerFillColorDefaultBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 8, 8, 8));
            RootGrid.Resources["ControlFillColorDefaultBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20));
            RootGrid.Resources["ControlFillColorSecondaryBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 28, 28, 28));
            RootGrid.Resources["ControlStrokeColorDefaultBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 45));
        }
        else
        {
            RootGrid.Background = null;
            ContentFrame.Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];

            RootGrid.Resources.Remove("ApplicationPageBackgroundThemeBrush");
            RootGrid.Resources.Remove("SolidBackgroundFillColorBaseBrush");
            RootGrid.Resources.Remove("CardBackgroundFillColorDefaultBrush");
            RootGrid.Resources.Remove("CardBackgroundFillColorSecondaryBrush");
            RootGrid.Resources.Remove("CardStrokeColorDefaultBrush");
            RootGrid.Resources.Remove("DividerStrokeColorDefaultBrush");
            RootGrid.Resources.Remove("LayerFillColorDefaultBrush");
            RootGrid.Resources.Remove("ControlFillColorDefaultBrush");
            RootGrid.Resources.Remove("ControlFillColorSecondaryBrush");
            RootGrid.Resources.Remove("ControlStrokeColorDefaultBrush");

            try
            {
                if (MicaController.IsSupported())
                    SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                else if (DesktopAcrylicController.IsSupported())
                    SystemBackdrop = new DesktopAcrylicBackdrop();
            }
            catch { }
        }

        ThemeSystemItem.IsChecked = _shellState.Theme == "System";
        ThemeLightItem.IsChecked = _shellState.Theme == "Light";
        ThemeDarkItem.IsChecked = _shellState.Theme == "Dark";
        ThemeOledItem.IsChecked = _shellState.Theme == "Oled";

        UpdateCaptionButtonColors();

        if (persist)
        {
            SaveShellState();
        }
    }

    public void ApplyShellTheme(string theme) => ApplyTheme(theme);

    public string CurrentShellTheme => _shellState.Theme;

    public void SyncAssistantToggle(bool isVisible)
    {
        AssistantToggleButton.IsChecked = isVisible;
        _shellState.IsAssistantVisible = isVisible;
        SaveShellState();
    }

    // ==========================================================================
    //  Badges dynamiques : profil enseignant & célébration
    // ==========================================================================
    private const string CelebratedFirstDocKey = "FicheGen.CelebratedFirstDoc";

    private void CelebrateFirstGeneration(ResultViewModel vm)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(vm.CurrentHtml)) return;

            var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (values.ContainsKey(CelebratedFirstDocKey)) return;
            values[CelebratedFirstDocKey] = true;

            ShowHint(Services.L10n.Get("Hint_FirstDoc_Title"),
                Services.L10n.Get("Hint_FirstDoc_Message"));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Célébration de première génération ignorée.");
        }
    }

    private void UpdateAiPulse(bool busy)
    {
        // Safe no-op after AI status chip removal from title bar
    }

    public void RefreshAiChip()
    {
        var readiness = App.Services.GetService<IReadinessService>();
        if (readiness == null) return;

        _ = readiness.RefreshAsync();
    }

    private void UpdateAiChipUi(IReadinessService readiness)
    {
        // Safe no-op
    }

    private void RefreshTeacherBadge()
    {
        try
        {
            var authService = App.Services.GetService<FicheGen.Core.Auth.IAuthService>();
            var settings = _settingsStore.GetSettings<AppSettings>();

            string name;
            string school;
            bool isAuth = authService?.IsAuthenticated == true && authService.CurrentUser != null;

            if (isAuth && authService!.CurrentUser is { } user)
            {
                name = !string.IsNullOrWhiteSpace(user.DisplayName)
                    ? user.DisplayName
                    : (!string.IsNullOrWhiteSpace(user.Nom) ? $"{user.Civilite} {user.Prenom} {user.Nom}".Trim() : user.Email);
                school = !string.IsNullOrWhiteSpace(user.SchoolName)
                    ? user.SchoolName
                    : "Compte Enseignant Actif";

                if (ProfileAccountLabel != null)
                {
                    ProfileAccountLabel.Text = $"Mon compte ({user.Email})";
                }
            }
            else
            {
                name = string.IsNullOrWhiteSpace(settings.Defaults.TeacherName) ? Services.L10n.Get("Profile_DefaultName") : settings.Defaults.TeacherName;
                school = string.IsNullOrWhiteSpace(settings.Defaults.SchoolName) ? Services.L10n.Get("Profile_DefaultSchool") : settings.Defaults.SchoolName;

                if (ProfileAccountLabel != null)
                {
                    ProfileAccountLabel.Text = "Connexion / Créer un compte";
                }
            }

            TeacherPersonPicture.DisplayName = name;
            TeacherPersonPictureLarge.DisplayName = name;
            ProfileNameText.Text = name;
            ProfileSubtext.Text = school;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Lecture du profil enseignant impossible.");
        }
    }
}
