using System.Text.RegularExpressions;

namespace FicheGen.Core.Toc;

public sealed record PageOffsetResult(
    int Offset,
    double Confidence,
    bool LowConfidenceWarning);

public static class PageOffsetDetector
{
    private static readonly Regex PageNumberRegex = new(@"^(?:Page\s*)?(\d{1,4})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static PageOffsetResult DetectOffset(IPageWordSource wordSource)
    {
        if (wordSource == null || wordSource.PageCount == 0)
            return new PageOffsetResult(0, 0.0, true);

        var startPage = Math.Min(12, wordSource.PageCount);
        var endPage = Math.Min(27, wordSource.PageCount);
        var pagesScanned = 0;

        var deltas = new List<int>();

        for (var physicalPage = startPage; physicalPage <= endPage; physicalPage++)
        {
            pagesScanned++;
            var words = wordSource.GetWords(physicalPage);
            if (words.Count == 0) continue;

            var pageHeight = words[0].PageHeight;
            if (pageHeight <= 0) continue;

            // Filter header (< 12%) and footer (> 88%) candidates
            var candidates = words.Where(w => w.Bottom < pageHeight * 0.12 || w.Bottom > pageHeight * 0.88).ToList();

            foreach (var word in candidates)
            {
                var match = PageNumberRegex.Match(word.Text.Trim());
                if (match.Success && int.TryParse(match.Groups[1].Value, out var printedPage) && printedPage > 0 && printedPage < 2000)
                {
                    var delta = physicalPage - printedPage;
                    deltas.Add(delta);
                }
            }
        }

        if (deltas.Count > 0)
        {
            var modeGroup = deltas.GroupBy(d => d).OrderByDescending(g => g.Count()).First();
            var modeOffset = modeGroup.Key;
            var agreeingCount = modeGroup.Count();

            if (agreeingCount >= 3)
            {
                var confidence = (double)agreeingCount / pagesScanned;
                return new PageOffsetResult(modeOffset, confidence, confidence < 0.5);
            }
        }

        // Fallback: whole-page standalone number scan
        for (var physicalPage = startPage; physicalPage <= endPage; physicalPage++)
        {
            var words = wordSource.GetWords(physicalPage);
            foreach (var word in words)
            {
                var match = PageNumberRegex.Match(word.Text.Trim());
                if (match.Success && int.TryParse(match.Groups[1].Value, out var printedPage) && printedPage > 0)
                {
                    deltas.Add(physicalPage - printedPage);
                }
            }
        }

        if (deltas.Count > 0)
        {
            var modeGroup = deltas.GroupBy(d => d).OrderByDescending(g => g.Count()).First();
            var modeOffset = modeGroup.Key;
            var confidence = (double)modeGroup.Count() / Math.Max(1, pagesScanned);
            return new PageOffsetResult(modeOffset, confidence, confidence < 0.5);
        }

        return new PageOffsetResult(0, 0.0, true);
    }
}
