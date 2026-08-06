using Serilog;

namespace FicheGen.Infrastructure.Diagnostics;

public static class SerilogBootstrap
{
    public static ILogger CreateLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.With(new SecretRedactingPolicy())
            .WriteTo.File(
                Path.Combine(logDirectory, "fichegen-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true)
            .CreateLogger();
    }
}
