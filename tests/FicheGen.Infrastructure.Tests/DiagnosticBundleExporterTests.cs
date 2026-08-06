using System.IO.Compression;
using FicheGen.Infrastructure.Services;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class DiagnosticBundleExporterTests
{
    [Fact]
    public async Task ExportDiagnosticBundleAsync_CreatesZipWithRedactedFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DiagTest_" + Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(tempDir, "logs");
        Directory.CreateDirectory(logDir);

        var logFile = Path.Combine(logDir, "fichegen-20260801.log");
        await File.WriteAllTextAsync(logFile, "Log entry: Authorization: Bearer my_secret_token_12345");

        var settingsFile = Path.Combine(tempDir, "settings.json");
        await File.WriteAllTextAsync(settingsFile, "{\"GeminiApiKey\":\"AIzaSyD1234567890123456789012345678901\"}");

        var zipPath = Path.Combine(tempDir, "bundle.zip");

        var exporter = new DiagnosticBundleExporter(logDir, settingsFile);
        var result = await exporter.ExportDiagnosticBundleAsync(zipPath);

        Assert.True(File.Exists(result));

        using (var archive = ZipFile.OpenRead(result))
        {
            Assert.Contains(archive.Entries, e => e.FullName.Contains("settings.json"));
            Assert.Contains(archive.Entries, e => e.FullName.Contains("fichegen-20260801.log"));

            var settingsEntry = archive.GetEntry("settings.json");
            Assert.NotNull(settingsEntry);
            using var reader = new StreamReader(settingsEntry!.Open());
            var text = await reader.ReadToEndAsync();
            Assert.DoesNotContain("AIzaSyD1234567890123456789012345678901", text);
        }

        Directory.Delete(tempDir, true);
    }
}
