using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Ai;
using FicheGen.Core.Documents;
using FicheGen.Core.Toc;
using Serilog;
using UglyToad.PdfPig;

namespace FicheGen.Infrastructure.Pdf;

public sealed class ParentDocumentIndexer
{
    private readonly TocCacheStore _cacheStore;
    private readonly ILlmClient? _llmClient;
    private readonly string _cacheDirectory;
    private readonly string _indexPath;
    private readonly object _syncLock = new();

    public ParentDocumentIndexer(TocCacheStore? cacheStore = null, ILlmClient? llmClient = null, string? cacheDir = null)
    {
        _cacheStore = cacheStore ?? new TocCacheStore(cacheDir);
        _llmClient = llmClient;
        _cacheDirectory = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen", "cache", "toc");
        _indexPath = Path.Combine(_cacheDirectory, "parent_documents_index.json");
    }

    public ParentDocumentsIndex? LoadIndex()
    {
        lock (_syncLock)
        {
            if (!File.Exists(_indexPath))
                return null;

            try
            {
                var json = File.ReadAllText(_indexPath, Encoding.UTF8);
                var dto = JsonSerializer.Deserialize<ParentDocumentsIndexDto>(json);
                if (dto == null) return null;

                var docs = dto.Documents.Select(d => new ParentDocumentItem(
                    d.FilePath,
                    d.FileName,
                    d.DropdownLabel,
                    d.DocumentType,
                    d.Level,
                    d.Extra,
                    d.Lessons.Select(l => new ToCEntry(l.Title, l.PrintedPage, l.PhysicalPage)).ToList()
                )).ToList();

                return new ParentDocumentsIndex(dto.LastScanUtc, dto.ParentDirectory, docs);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Impossible de charger l'index des documents parents.");
                return null;
            }
        }
    }

    public void SaveIndex(ParentDocumentsIndex index)
    {
        lock (_syncLock)
        {
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                var dto = new ParentDocumentsIndexDto
                {
                    LastScanUtc = index.LastScanUtc,
                    ParentDirectory = index.ParentDirectory,
                    Documents = index.Documents.Select(d => new ParentDocumentItemDto
                    {
                        FilePath = d.FilePath,
                        FileName = d.FileName,
                        DropdownLabel = d.DropdownLabel,
                        DocumentType = d.DocumentType,
                        Level = d.Level,
                        Extra = d.Extra,
                        Lessons = d.Lessons.Select(l => new ToCEntryDto
                        {
                            Title = l.Title,
                            PrintedPage = l.PrintedPage,
                            PhysicalPage = l.PhysicalPage
                        }).ToList()
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                var tmp = $"{_indexPath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    File.WriteAllText(tmp, json, Encoding.UTF8);

                    const int maxRetries = 3;
                    for (var attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            File.Move(tmp, _indexPath, overwrite: true);
                            break;
                        }
                        catch (IOException) when (attempt < maxRetries)
                        {
                            Thread.Sleep(50 * attempt);
                        }
                    }
                }
                finally
                {
                    if (File.Exists(tmp))
                    {
                        try { File.Delete(tmp); } catch { /* ignore cleanup errors */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Échec de l'enregistrement de l'index des documents parents.");
            }
        }
    }

    /// <summary>
    /// Énumère récursivement tous les fichiers PDF du dossier parent,
    /// détecte leur niveau et type, et vérifie s'ils ont déjà une table des matières en cache.
    /// Ne lance pas d'extraction lourde par IA.
    /// </summary>
    public IReadOnlyList<ParentDocumentItem> DiscoverDocuments(string parentDocsDir)
    {
        if (string.IsNullOrWhiteSpace(parentDocsDir) || !Directory.Exists(parentDocsDir))
        {
            return Array.Empty<ParentDocumentItem>();
        }

        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        var pdfFiles = Directory.EnumerateFiles(parentDocsDir, "*.pdf", enumOptions)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingIndex = LoadIndex();
        var existingMap = existingIndex?.Documents
            .ToDictionary(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ParentDocumentItem>(StringComparer.OrdinalIgnoreCase);

        var result = new List<ParentDocumentItem>();
        foreach (var file in pdfFiles)
        {
            if (existingMap.TryGetValue(file, out var existingDoc) && existingDoc.Lessons.Count > 0)
            {
                result.Add(existingDoc);
            }
            else
            {
                var cached = _cacheStore.TryGetCached(file);
                var lessons = cached?.Entries ?? (IReadOnlyList<ToCEntry>)Array.Empty<ToCEntry>();
                var level = LevelDetector.DetectLevel(file) ?? string.Empty;
                var docType = LevelDetector.DetectDocumentType(Path.GetFileName(file));
                var label = LevelDetector.GenerateDefaultDropdownLabel(file);
                result.Add(new ParentDocumentItem(file, Path.GetFileName(file), label, docType, level, string.Empty, lessons));
            }
        }

        return result;
    }

    /// <summary>
    /// Extrait la table des matières d'un seul document PDF (priorité IA avec repli hors-ligne),
    /// l'enregistre dans le cache et met à jour l'index global des documents parents.
    /// </summary>
    public async Task<ParentDocumentItem?> IndexSingleDocumentAndSaveAsync(
        string parentDocsDir,
        string pdfPath,
        AiRequestConfig? aiConfig = null,
        Func<string, Task<bool>>? confirmOfflineFallback = null,
        IProgress<string>? progress = null,
        bool forceRescan = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            return null;

        var fileName = Path.GetFileName(pdfPath);
        progress?.Report($"Analyse de la table des matières pour {fileName}…");

        var docItem = await IndexSingleDocumentAsync(pdfPath, aiConfig, confirmOfflineFallback, forceRescan, ct).ConfigureAwait(false);
        if (docItem == null) return null;

        lock (_syncLock)
        {
            var currentIndex = LoadIndex();
            var docsList = currentIndex?.Documents.ToList() ?? new List<ParentDocumentItem>();
            docsList.RemoveAll(d => string.Equals(d.FilePath, pdfPath, StringComparison.OrdinalIgnoreCase));
            docsList.Add(docItem);

            var dir = !string.IsNullOrWhiteSpace(parentDocsDir) && Directory.Exists(parentDocsDir)
                ? parentDocsDir
                : (currentIndex?.ParentDirectory ?? Path.GetDirectoryName(pdfPath) ?? string.Empty);

            var updatedIndex = new ParentDocumentsIndex(DateTime.UtcNow, dir, docsList);
            SaveIndex(updatedIndex);
        }

        progress?.Report($"Table des matières extraite : {docItem.Lessons.Count} leçon(s) prête(s).");
        return docItem;
    }

    public async Task<ParentDocumentsIndex> IndexAllAsync(
        string parentDocsDir,
        AiRequestConfig? aiConfig = null,
        Func<string, Task<bool>>? confirmOfflineFallback = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentDocsDir) || !Directory.Exists(parentDocsDir))
        {
            return new ParentDocumentsIndex(DateTime.UtcNow, parentDocsDir ?? string.Empty, Array.Empty<ParentDocumentItem>());
        }

        // 1. Discover all PDF files recursively
        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        var pdfFiles = Directory.EnumerateFiles(parentDocsDir, "*.pdf", enumOptions)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        progress?.Report($"Recherche terminée : {pdfFiles.Count} document(s) trouvé(s).");

        var indexedDocs = new List<ParentDocumentItem>();
        var total = pdfFiles.Count;

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var pdfPath = pdfFiles[i];
            var fileName = Path.GetFileName(pdfPath);

            progress?.Report($"[{i + 1}/{total}] Analyse de {fileName}…");

            try
            {
                var docItem = await IndexSingleDocumentAsync(pdfPath, aiConfig, confirmOfflineFallback, forceRescan: false, ct).ConfigureAwait(false);
                if (docItem != null)
                {
                    indexedDocs.Add(docItem);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Erreur lors de l'indexation de {File}.", fileName);
            }
        }

        var resultIndex = new ParentDocumentsIndex(DateTime.UtcNow, parentDocsDir, indexedDocs);
        SaveIndex(resultIndex);
        progress?.Report($"Indexation terminée : {indexedDocs.Count} document(s) enregistré(s).");

        return resultIndex;
    }

    private async Task<ParentDocumentItem?> IndexSingleDocumentAsync(
        string pdfPath,
        AiRequestConfig? aiConfig,
        Func<string, Task<bool>>? confirmOfflineFallback,
        bool forceRescan,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(pdfPath);
        var cached = _cacheStore.TryGetCached(pdfPath);
        var pdfHash = TocCacheStore.ComputePdfHash(pdfPath);

        // If cache exists and has entries, and we are not forcing a rescan, use cached data
        if (!forceRescan && cached != null && cached.Entries.Count > 0)
        {
            var level = LevelDetector.DetectLevel(pdfPath) ?? string.Empty;
            var docType = LevelDetector.DetectDocumentType(fileName);
            var label = LevelDetector.GenerateDefaultDropdownLabel(pdfPath);
            return new ParentDocumentItem(pdfPath, fileName, label, docType, level, string.Empty, cached.Entries);
        }

        // Try AI extraction first if LLM client is available
        AiTocExtractionResult? aiResult = null;

        if (_llmClient != null && aiConfig != null)
        {
            try
            {
                aiResult = await ExtractTocWithAiAsync(pdfPath, aiConfig, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Échec de l'extraction IA pour {File}.", fileName);
                aiResult = null;
            }
        }

        // If AI succeeded
        if (aiResult != null && aiResult.Entries.Count > 0)
        {
            int pageCount;
            try
            {
                using var doc = PdfDocument.Open(pdfPath);
                pageCount = doc.NumberOfPages;
            }
            catch
            {
                pageCount = 1000;
            }

            var tocEntries = aiResult.Entries.Select(e =>
            {
                var phys = e.PhysicalPage > 0 ? e.PhysicalPage : Math.Clamp(e.PrintedPage + aiResult.Offset, 1, pageCount);
                return new ToCEntry(e.Title, e.PrintedPage, phys);
            }).ToList();

            var tocResult = new TocResult(
                pdfPath,
                pdfHash,
                aiResult.Offset,
                OffsetConfidence: 0.95,
                LowConfidenceWarning: false,
                IsScanned: false,
                tocEntries
            );
            _cacheStore.SaveCache(tocResult);

            var label = string.IsNullOrWhiteSpace(aiResult.DropdownLabel)
                ? LevelDetector.GenerateDefaultDropdownLabel(pdfPath)
                : aiResult.DropdownLabel;

            var level = string.IsNullOrWhiteSpace(aiResult.Level)
                ? (LevelDetector.DetectLevel(pdfPath) ?? string.Empty)
                : aiResult.Level;

            var docType = string.IsNullOrWhiteSpace(aiResult.DocumentType)
                ? LevelDetector.DetectDocumentType(fileName)
                : aiResult.DocumentType;

            return new ParentDocumentItem(pdfPath, fileName, label, docType, level, aiResult.Extra ?? string.Empty, tocEntries);
        }

        // Offline heuristics should proceed after the model doesn't respond or produces no entries,
        // and even then, only proceed after the app shows the confirmation prompt.
        if (confirmOfflineFallback == null)
        {
            Log.Information("L'extraction IA n'a pas retourné de résultat pour {File}, repli hors-ligne ignoré car non confirmé.", fileName);
            return null;
        }

        var runOffline = await confirmOfflineFallback(fileName).ConfigureAwait(false);
        if (!runOffline)
        {
            Log.Information("Repli hors-ligne annulé par l'utilisateur pour {File}.", fileName);
            return null;
        }

        // Run local offline extraction fallback only after explicit confirmation
        var offlineItem = ExtractTocOffline(pdfPath, pdfHash);
        if (offlineItem == null || offlineItem.Lessons.Count == 0)
        {
            return null;
        }

        return offlineItem;
    }

    private ParentDocumentItem ExtractTocOffline(string pdfPath, string pdfHash)
    {
        var fileName = Path.GetFileName(pdfPath);
        var level = LevelDetector.DetectLevel(pdfPath) ?? string.Empty;
        var docType = LevelDetector.DetectDocumentType(fileName);
        var label = LevelDetector.GenerateDefaultDropdownLabel(pdfPath);

        using var document = PdfDocument.Open(pdfPath);
        var pageCount = document.NumberOfPages;

        // Search front pages (up to 20) and back pages
        var candidatePages = Math.Min(20, pageCount);
        var sb = new StringBuilder();
        for (var p = 1; p <= candidatePages; p++)
        {
            sb.AppendLine(ExtractPageTextWithLines(document.GetPage(p)));
        }

        var parsed = TocParser.ParseToc(sb.ToString());

        if (parsed.Count == 0 && pageCount > candidatePages)
        {
            var startBack = Math.Max(candidatePages + 1, pageCount - 15);
            var backSb = new StringBuilder();
            for (var p = startBack; p <= pageCount; p++)
            {
                backSb.AppendLine(ExtractPageTextWithLines(document.GetPage(p)));
            }
            parsed = TocParser.ParseToc(backSb.ToString());
        }

        if (parsed.Count == 0)
        {
            return new ParentDocumentItem(pdfPath, fileName, label, docType, level, string.Empty, Array.Empty<ToCEntry>());
        }

        using var wordSource = new PdfPigPageWordSource(document);
        var offsetResult = PageOffsetDetector.DetectOffset(wordSource);

        var entries = new List<ToCEntry>();
        foreach (var (title, printedPage) in parsed)
        {
            var physicalPage = Math.Clamp(printedPage + offsetResult.Offset, 1, pageCount);
            entries.Add(new ToCEntry(title, printedPage, physicalPage));
        }

        var tocResult = new TocResult(
            pdfPath,
            pdfHash,
            offsetResult.Offset,
            offsetResult.Confidence,
            offsetResult.LowConfidenceWarning,
            IsScanned: false,
            entries
        );

        _cacheStore.SaveCache(tocResult);

        return new ParentDocumentItem(pdfPath, fileName, label, docType, level, string.Empty, entries);
    }

    private async Task<AiTocExtractionResult?> ExtractTocWithAiAsync(
        string pdfPath,
        AiRequestConfig aiConfig,
        CancellationToken ct)
    {
        if (_llmClient == null) return null;

        using var document = PdfDocument.Open(pdfPath);
        var pageCount = document.NumberOfPages;

        // User requirement: Extract the content of the first 10 and last 10 pages
        var pagesToExtract = new SortedSet<int>();
        var frontLimit = Math.Min(10, pageCount);
        for (var p = 1; p <= frontLimit; p++)
        {
            pagesToExtract.Add(p);
        }
        var backStart = Math.Max(1, pageCount - 9);
        for (var p = backStart; p <= pageCount; p++)
        {
            pagesToExtract.Add(p);
        }

        // Budget safe characters to prevent Supabase 422 payload/message_too_long error (max 50k chars)
        const int maxTotalChars = 40000;
        var perPageBudget = maxTotalChars / Math.Max(1, pagesToExtract.Count);

        var sb = new StringBuilder();
        foreach (var p in pagesToExtract)
        {
            var pageText = ExtractPageTextWithLines(document.GetPage(p));
            if (string.IsNullOrWhiteSpace(pageText)) continue;

            if (pageText.Length > perPageBudget)
            {
                pageText = pageText.Substring(0, perPageBudget) + "\n[...suite tronquée...]";
            }

            sb.AppendLine($"--- Page Physique {p} (sur {pageCount}) ---");
            sb.AppendLine(pageText);
            sb.AppendLine();
        }

        var fileName = Path.GetFileName(pdfPath);
        var prompt = $$"""
Fichier : {{fileName}}
Nombre total de pages physiques : {{pageCount}}

Contenu extrait (10 premières pages et 10 dernières pages) :
{{sb}}

Consignes :
1. Recherche la table des matières complète (titrée généralement « Sommaire », « Table des matières », « Contenu », « Sommaire général » ou « Plan ») présente dans ces pages de début ou de fin du document.
2. Extrais TOUTES les leçons, fiches, chapitres, séances ou unités d'apprentissage listées dans la table des matières.
3. Pour chaque entrée :
   - "title" : le titre exact et complet de la fiche/leçon (ex: "Fiche n° 01 : L'air, une source d'énergie")
   - "printed_page" : le numéro de page imprimé indiqué dans la table des matières (ex: 7)
   - "physical_page" : le numéro de page physique réel dans le fichier PDF (égal à printed_page + offset)
4. Calcule précisément "offset" (la différence entre le numéro de page physique dans le PDF et le numéro de page imprimé, souvent entre 0 et 8).
5. Détermine le niveau scolaire français ("level" : CP, CE1, CE2, CM1, CM2, 6e, 5e, etc.), le type ("document_type" : "Guide", "Manuel", ou "Cahier") et la matière ou collection ("extra").
6. Format du label dropdown attendu : "{document_type}-{level}-{extra}" (ex: "Guide-CM1-Sciences ABC" ou "Manuel-6e-Maths").

Réponds EXCLUSIVEMENT avec un objet JSON structuré comme suit :
{
  "document_type": "Guide",
  "level": "CM1",
  "extra": "Sciences ABC",
  "dropdown_label": "Guide-CM1-Sciences ABC",
  "offset": 0,
  "entries": [
    {
      "title": "Fiche n° 01 : L'air, une source d'énergie",
      "printed_page": 7,
      "physical_page": 7
    }
  ]
}
""";

        var req = new LlmRequest(
            Purpose: "toc",
            SystemPrompt: "Tu es un assistant expert dans l'analyse de manuels et guides pédagogiques scolaires français. Tu extrais fidèlement et exhaustivement la table des matières et le niveau.",
            UserPrompt: prompt,
            Temperature: 0.1,
            ResponseJson: true
        );

        var responseJson = await _llmClient.GenerateAsync(req, aiConfig, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseJson)) return null;

        var cleaned = JsonCleaner.Clean(responseJson);

        // Extract JSON between first '{' and last '}' to strip any surrounding text
        var firstBrace = cleaned.IndexOf('{');
        var lastBrace = cleaned.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        var result = JsonSerializer.Deserialize<AiTocExtractionResult>(cleaned, options);
        if (result == null || result.Entries == null) return null;

        // Auto-repair physical page if missing or 0
        foreach (var entry in result.Entries)
        {
            if (entry.PhysicalPage <= 0 && entry.PrintedPage > 0)
            {
                entry.PhysicalPage = Math.Clamp(entry.PrintedPage + result.Offset, 1, pageCount);
            }
        }

        return result;
    }

    private static string ExtractPageTextWithLines(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords();
        if (words == null || !words.Any())
            return page.Text ?? string.Empty;

        var lines = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 5.0) * 5.0)
            .OrderByDescending(g => g.Key)
            .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));

        return string.Join("\n", lines);
    }

    private sealed class ParentDocumentsIndexDto
    {
        public DateTime LastScanUtc { get; set; }
        public string ParentDirectory { get; set; } = string.Empty;
        public List<ParentDocumentItemDto> Documents { get; set; } = new();
    }

    private sealed class ParentDocumentItemDto
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DropdownLabel { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string Extra { get; set; } = string.Empty;
        public List<ToCEntryDto> Lessons { get; set; } = new();
    }

    private sealed class AiTocExtractionResult
    {
        [JsonPropertyName("document_type")]
        public string? DocumentType { get; set; }

        [JsonPropertyName("level")]
        public string? Level { get; set; }

        [JsonPropertyName("extra")]
        public string? Extra { get; set; }

        [JsonPropertyName("dropdown_label")]
        public string? DropdownLabel { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("entries")]
        public List<AiTocEntryDto> Entries { get; set; } = new();
    }

    private sealed class AiTocEntryDto
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("printed_page")]
        public int PrintedPage { get; set; }

        [JsonPropertyName("physical_page")]
        public int PhysicalPage { get; set; }
    }
}

