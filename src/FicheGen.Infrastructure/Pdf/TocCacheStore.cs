using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Toc;
using Serilog;

namespace FicheGen.Infrastructure.Pdf;

public sealed class TocCacheDto
{
    public int Version { get; set; } = 2;
    public string PdfPath { get; set; } = string.Empty;
    public string PdfHash { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; }
    public int Offset { get; set; }
    public double OffsetConfidence { get; set; }
    public bool LowConfidenceWarning { get; set; }
    public bool IsScanned { get; set; }
    public List<ToCEntryDto> Entries { get; set; } = new();
}

public sealed class ToCEntryDto
{
    public string Title { get; set; } = string.Empty;
    public int PrintedPage { get; set; }
    public int PhysicalPage { get; set; }
}

public sealed class TocCacheStore
{
    private readonly string _cacheDirectory;
    private readonly object _syncLock = new();

    public TocCacheStore(string? cacheDir = null)
    {
        _cacheDirectory = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen", "cache", "toc");

        Directory.CreateDirectory(_cacheDirectory);
        CleanOrphanedTempFiles();
    }

    private void CleanOrphanedTempFiles()
    {
        try
        {
            var tmpFiles = Directory.EnumerateFiles(_cacheDirectory, "*.tmp");
            foreach (var file in tmpFiles)
            {
                try
                {
                    var info = new FileInfo(file);
                    // Only delete abandoned temp files older than 5 minutes to avoid racing active writes
                    if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromMinutes(5))
                    {
                        File.Delete(file);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public static string ComputePdfHash(string pdfPath)
    {
        if (!File.Exists(pdfPath))
            return string.Empty;

        var fileInfo = new FileInfo(pdfPath);
        var input = $"{pdfPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return hex.Substring(0, Math.Min(16, hex.Length));
    }

    public string GetCachePath(string pdfPath)
    {
        var hash = ComputePdfHash(pdfPath);
        return Path.Combine(_cacheDirectory, $"{hash}.json");
    }

    public TocResult? TryGetCached(string pdfPath)
    {
        var cachePath = GetCachePath(pdfPath);
        if (!File.Exists(cachePath))
            return null;

        try
        {
            string json;
            using (var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }

            var dto = JsonSerializer.Deserialize<TocCacheDto>(json);
            if (dto == null || dto.Version != 2)
                return null;

            var currentHash = ComputePdfHash(pdfPath);
            if (dto.PdfHash != currentHash)
                return null;

            var entries = dto.Entries.Select(e => new ToCEntry(e.Title, e.PrintedPage, e.PhysicalPage)).ToList();

            return new TocResult(
                dto.PdfPath,
                dto.PdfHash,
                dto.Offset,
                dto.OffsetConfidence,
                dto.LowConfidenceWarning,
                dto.IsScanned,
                entries
            );
        }
        catch (JsonException ex)
        {
            // Quarantine corrupt file only when deserialization fails (invalid/corrupted JSON)
            Log.Warning(ex, "Cache ToC corrompu pour {PdfPath}, mise en quarantaine.", pdfPath);
            try
            {
                var corruptPath = $"{cachePath}.corrupt-{DateTime.UtcNow.Ticks}";
                File.Move(cachePath, corruptPath, overwrite: true);
            }
            catch { }
            return null;
        }
        catch (Exception ex)
        {
            // Transient read or file sharing exception
            Log.Debug(ex, "Impossible de lire le cache ToC pour {PdfPath}.", pdfPath);
            return null;
        }
    }

    public void SaveCache(TocResult result)
    {
        var cachePath = GetCachePath(result.PdfPath);
        var dto = new TocCacheDto
        {
            Version = 2,
            PdfPath = result.PdfPath,
            PdfHash = result.PdfHash,
            GeneratedUtc = DateTime.UtcNow,
            Offset = result.Offset,
            OffsetConfidence = result.OffsetConfidence,
            LowConfidenceWarning = result.LowConfidenceWarning,
            IsScanned = result.IsScanned,
            Entries = result.Entries.Select(e => new ToCEntryDto
            {
                Title = e.Title,
                PrintedPage = e.PrintedPage,
                PhysicalPage = e.PhysicalPage
            }).ToList()
        };

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        // Use a unique temporary filename per write so concurrent operations never collide
        var tmpPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            File.WriteAllText(tmpPath, json, Encoding.UTF8);

            // Retry briefly on transient file locks when moving the file into place
            const int maxRetries = 3;
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    lock (_syncLock)
                    {
                        File.Move(tmpPath, cachePath, overwrite: true);
                    }
                    break;
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    Thread.Sleep(20 * attempt);
                }
            }
        }
        catch (Exception ex)
        {
            // Cache saving is an optimization and must never crash the application.
            Log.Debug(ex, "Échec de l'enregistrement dans le cache ToC pour {PdfPath}.", result.PdfPath);
        }
        finally
        {
            try
            {
                if (File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }
            }
            catch { }
        }
    }
}
