using System.IO.Compression;
using System.Text.Json;
using FicheGen.Core.Abstractions;
using FicheGen.Infrastructure.Diagnostics;

namespace FicheGen.Infrastructure.Services;

public class DiagnosticBundleExporter : IDiagnosticBundleExporter
{
    private readonly string _logDir;
    private readonly string _settingsFilePath;

    public DiagnosticBundleExporter(string? logDir = null, string? settingsFilePath = null)
    {
        _logDir = logDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen",
            "logs");

        _settingsFilePath = settingsFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen",
            "settings.json");
    }

    public async Task<string> ExportDiagnosticBundleAsync(string destinationZipPath, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FicheGen_Diag_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Copy & Redact Logs
            var logsTempDir = Path.Combine(tempDir, "logs");
            Directory.CreateDirectory(logsTempDir);

            if (Directory.Exists(_logDir))
            {
                foreach (var logFile in Directory.GetFiles(_logDir, "*.log"))
                {
                    var destLogFile = Path.Combine(logsTempDir, Path.GetFileName(logFile));
                    var rawContent = await File.ReadAllTextAsync(logFile, ct);
                    var redactedContent = SecretRedactingPolicy.RedactText(rawContent);
                    await File.WriteAllTextAsync(destLogFile, redactedContent, ct);
                }
            }

            // Copy & Redact Settings
            var settingsDestPath = Path.Combine(tempDir, "settings.json");
            if (File.Exists(_settingsFilePath))
            {
                var settingsRaw = await File.ReadAllTextAsync(_settingsFilePath, ct);
                var settingsRedacted = SecretRedactingPolicy.RedactText(settingsRaw);
                await File.WriteAllTextAsync(settingsDestPath, settingsRedacted, ct);
            }
            else
            {
                await File.WriteAllTextAsync(settingsDestPath, "{\"notice\":\"No custom settings file found\"}", ct);
            }

            // Create Zip
            if (File.Exists(destinationZipPath))
            {
                File.Delete(destinationZipPath);
            }

            ZipFile.CreateFromDirectory(tempDir, destinationZipPath);
            return destinationZipPath;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
