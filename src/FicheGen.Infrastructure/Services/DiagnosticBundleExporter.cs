using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
                    var rawContent = await File.ReadAllTextAsync(logFile, ct).ConfigureAwait(false);
                    var redactedContent = SecretRedactingPolicy.RedactText(MaskPaths(rawContent));
                    await File.WriteAllTextAsync(destLogFile, redactedContent, ct).ConfigureAwait(false);
                }
            }

            // Copy & Redact Settings
            var settingsDestPath = Path.Combine(tempDir, "settings.json");
            if (File.Exists(_settingsFilePath))
            {
                var settingsRaw = await File.ReadAllTextAsync(_settingsFilePath, ct).ConfigureAwait(false);
                var sanitizedSettings = SanitizeSettingsJson(settingsRaw);
                var settingsRedacted = SecretRedactingPolicy.RedactText(sanitizedSettings);
                await File.WriteAllTextAsync(settingsDestPath, settingsRedacted, ct).ConfigureAwait(false);
            }
            else
            {
                await File.WriteAllTextAsync(settingsDestPath, "{\"notice\":\"No custom settings file found\"}", ct).ConfigureAwait(false);
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

    private static string SanitizeSettingsJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                if (obj["defaults"] is JsonObject defaults)
                {
                    if (defaults.ContainsKey("teacherName")) defaults["teacherName"] = "[REDACTED_TEACHER]";
                    if (defaults.ContainsKey("schoolName")) defaults["schoolName"] = "[REDACTED_SCHOOL]";
                }
                if (obj["folders"] is JsonObject folders)
                {
                    if (folders["guidesDir"] is JsonValue gVal && gVal.TryGetValue<string>(out var gDir))
                        folders["guidesDir"] = MaskUserPath(gDir);
                    if (folders["exportsDir"] is JsonValue eVal && eVal.TryGetValue<string>(out var eDir))
                        folders["exportsDir"] = MaskUserPath(eDir);
                }
                return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch
        {
            // Fallback to text masking if JSON parsing fails
        }
        return MaskPaths(json);
    }

    private static string MaskPaths(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, @"([a-zA-Z]:\\Users\\)[^\\]+", "$1[USER]", RegexOptions.IgnoreCase);
    }

    private static string MaskUserPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile) && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            return "%USERPROFILE%" + path[userProfile.Length..];
        }
        return MaskPaths(path);
    }
}
