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
                services.AddSingleton<IClock, SystemClock>();
                services.AddSingleton<IHistoryRepository, HistoryRepository>();
                services.AddSingleton<StylePresetService>();
                services.AddSingleton<PickerService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<IDiagnosticBundleExporter, DiagnosticBundleExporter>();

                services.AddHttpClient();
                services.AddSingleton<LlmRouter>();
                services.AddSingleton<ILlmClient, LlmClient>();
                services.AddSingleton<IPdfGuideService, PdfGuideService>();
                services.AddSingleton<IDocxExporter, DocxExporter>();
                services.AddSingleton<IRtfDocumentWriter, RtfDocumentWriter>();
                services.AddSingleton<IDocumentPdfExporter, WebView2PdfExporter>();

                services.AddSingleton<GenerationOrchestrator>();
                services.AddSingleton<IAssistantService, AssistantService>();

                // ViewModels
                services.AddSingleton<ResultViewModel>();
                services.AddTransient<FicheFormViewModel>();
                services.AddTransient<EvaluationViewModel>();
                services.AddTransient<QuizViewModel>();
                services.AddTransient<AssistantViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<SettingsViewModel>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();

        // Initialize PickerService with MainWindow handle
        var pickerService = Services.GetRequiredService<PickerService>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        pickerService.Initialize(hwnd);

        MainWindow.Activate();

        await _host.StartAsync();

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
