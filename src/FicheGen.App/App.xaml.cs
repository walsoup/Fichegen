using FicheGen.App.Services;
using FicheGen.App.ViewModels;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Ai;
using FicheGen.Core.Services;
using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Ai;
using FicheGen.Infrastructure.Diagnostics;
using FicheGen.Infrastructure.Export;
using FicheGen.Infrastructure.Pdf;
using FicheGen.Infrastructure.Security;
using FicheGen.Infrastructure.Services;
using FicheGen.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Serilog;

namespace FicheGen.App;

public partial class App : Application
{
    private readonly IHost _host;
    public static IServiceProvider Services => ((App)Current)._host.Services;

    public MainWindow? MainWindow { get; private set; }
    public static MainWindow? CurrentMainWindow => (Current as App)?.MainWindow;

    public App()
    {
        InitializeComponent();

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen",
            "logs");

        Log.Logger = SerilogBootstrap.CreateLogger(logDirectory);

        SetupExceptionHandling();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                // Core & Infrastructure Services
                services.AddSingleton<ICredentialStore, CredentialLockerStore>();
                services.AddSingleton<ISettingsStore, SettingsStore>();
                services.AddSingleton<FicheGen.Core.Auth.IAuthService, FicheGen.Infrastructure.Auth.SupabaseAuthService>();
                services.AddSingleton<IClock, SystemClock>();
                services.AddSingleton<IHistoryRepository, HistoryRepository>();
                services.AddSingleton<IDraftStore, FileDraftStore>();
                services.AddSingleton<StylePresetService>();
                services.AddSingleton<PickerService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<AccentColorService>();
                services.AddSingleton<IDiagnosticBundleExporter, DiagnosticBundleExporter>();

                services.AddHttpClient();
                services.AddSingleton<LlmRouter>();
                services.AddSingleton<ILlmClient, LlmClient>();
                services.AddSingleton<IConnectionTester, ConnectionTester>();
                services.AddSingleton<IProxyModelScanner, ProxyModelScanner>();
                services.AddSingleton<IOcrService, FicheGen.Infrastructure.Ocr.WindowsNativeOcrService>();
                services.AddSingleton<TocCacheStore>();
                services.AddSingleton<ParentDocumentIndexer>(sp => new ParentDocumentIndexer(
                    sp.GetRequiredService<TocCacheStore>(),
                    sp.GetService<ILlmClient>()));
                services.AddSingleton<IPdfGuideService, PdfGuideService>();
                services.AddSingleton<IDocxExporter, DocxExporter>();
                services.AddSingleton<IRtfDocumentWriter, RtfDocumentWriter>();
                services.AddSingleton<IDocumentPdfExporter, WebView2PdfExporter>();
                services.AddSingleton<IExportWorkflowService, ExportWorkflowService>();

                services.AddSingleton<GenerationOrchestrator>();
                services.AddSingleton<IAssistantService, AssistantService>();
                services.AddSingleton<IReadinessService, ReadinessService>();

                // ViewModels
                services.AddSingleton<ResultViewModel>();
                services.AddSingleton<FicheFormViewModel>();
                services.AddSingleton<EvaluationViewModel>();
                services.AddSingleton<QuizViewModel>();
                services.AddSingleton<AssistantViewModel>();
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddTransient<AccountViewModel>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Applique la langue persistée avant la création de la fenêtre.
        try
        {
            var settings = Services.GetRequiredService<ISettingsStore>().GetSettings<FicheGen.Core.Storage.AppSettings>();
            if (!string.IsNullOrWhiteSpace(settings.Ui.Language))
            {
                L10n.SetLanguage(settings.Ui.Language);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Application de la langue au démarrage impossible.");
        }

        // Applique la couleur d'accentuation persistée avant la création de la fenêtre,
        // pour que la première frame utilise déjà la teinte choisie.
        try
        {
            Services.GetRequiredService<AccentColorService>().ApplyFromSettings();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Application de la couleur d'accentuation au démarrage impossible.");
        }

        // Réenregistre le style « Personnalisé » du StyleBuilder pour le moteur de rendu.
        try
        {
            CustomPresetBootstrapper.Register(
                Services.GetRequiredService<StylePresetService>(),
                Services.GetRequiredService<ISettingsStore>());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Réenregistrement du style personnalisé impossible.");
        }

        MainWindow = new MainWindow();

        // Initialize PickerService with MainWindow handle
        var pickerService = Services.GetRequiredService<PickerService>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        pickerService.Initialize(hwnd);

        MainWindow.Activate();

        await _host.StartAsync();

        // Restauration de la session enseignant en arrière-plan
        _ = Task.Run(async () =>
        {
            try
            {
                var authService = Services.GetRequiredService<FicheGen.Core.Auth.IAuthService>();
                await authService.RestoreSessionAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Restauration de la session enseignant impossible au démarrage.");
            }
        });

        // Background retention policy sweep
        _ = Task.Run(async () =>
        {
            try
            {
                var settingsStore = Services.GetRequiredService<ISettingsStore>();
                var historyRepo = Services.GetRequiredService<IHistoryRepository>();
                var retentionDays = settingsStore.GetSettings<AppSettings>().Features.HistoryRetentionDays;
                if (retentionDays > 0)
                {
                    await historyRepo.CleanupRetentionAsync(retentionDays);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background history retention sweep failed");
            }
        });
    }

    private void SetupExceptionHandling()
    {
        UnhandledException += (sender, e) =>
        {
            Log.Fatal(e.Exception, "Unhandled UI exception");
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled AppDomain exception");
            }
        };
    }
}
