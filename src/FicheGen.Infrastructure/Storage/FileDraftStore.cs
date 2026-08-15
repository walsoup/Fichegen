using System.Text.Json;
using FicheGen.Core.Abstractions;

namespace FicheGen.Infrastructure.Storage;

/// <summary>
/// Implémentation de persistance des brouillons de formulaire sur disque (fichiers JSON atomiques).
/// </summary>
public sealed class FileDraftStore : IDraftStore
{
    private readonly string _draftsDirectory;

    public FileDraftStore(string? baseDirectory = null)
    {
        _draftsDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen",
            "drafts");

        try
        {
            Directory.CreateDirectory(_draftsDirectory);
        }
        catch
        {
            // Ignoré si le répertoire existe déjà ou droits limités
        }
    }

    public async Task SaveDraftAsync(
        string draftKey,
        IReadOnlyDictionary<string, string?> fields,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftKey)) return;

        try
        {
            Directory.CreateDirectory(_draftsDirectory);
            var filePath = GetDraftFilePath(draftKey);
            var tempPath = $"{filePath}.tmp";

            var json = JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, null);
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        catch
        {
            // La sauvegarde de brouillon ne doit jamais faire planter l'application
        }
    }

    public async Task<IReadOnlyDictionary<string, string?>?> LoadDraftAsync(
        string draftKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftKey)) return null;

        var filePath = GetDraftFilePath(draftKey);
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return null;

            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
        }
        catch
        {
            return null;
        }
    }

    public Task ClearDraftAsync(
        string draftKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftKey)) return Task.CompletedTask;

        try
        {
            var filePath = GetDraftFilePath(draftKey);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Non bloquant
        }

        return Task.CompletedTask;
    }

    private string GetDraftFilePath(string draftKey)
    {
        var safeKey = string.Join("_", draftKey.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_draftsDirectory, $"{safeKey}.json");
    }
}
