using System;
using System.IO;
using System.Text.RegularExpressions;

namespace FicheGen.Core.Toc;

public static class LevelDetector
{
    private static readonly Regex Re6e = new(@"\b(?:6e|6ème|6eme|sixi[eè]me|1ac)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Re5e = new(@"\b(?:5e|5ème|5eme|cinqui[eè]me|2ac)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Re4e = new(@"\b(?:4e|4ème|4eme|quatri[eè]me|3ac)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Re3e = new(@"\b(?:3e|3ème|3eme|troisi[eè]me)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReCm2 = new(@"\b(?:cm2|cm\s*2|cours\s*moyen\s*2)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReCm1 = new(@"\b(?:cm1|cm\s*1|cours\s*moyen\s*1)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReCe2 = new(@"\b(?:ce2|ce\s*2|cours\s*[eé]l[eé]mentaire\s*2)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReCe1 = new(@"\b(?:ce1|ce\s*1|cours\s*[eé]l[eé]mentaire\s*1)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReCp = new(@"\b(?:cp|cours\s*pr[eé]paratoire)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReGs = new(@"\b(?:gs|grande\s*section)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReMs = new(@"\b(?:ms|moyenne\s*section)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RePs = new(@"\b(?:ps|petite\s*section)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? DetectLevel(string filePathOrName)
    {
        if (string.IsNullOrWhiteSpace(filePathOrName))
            return null;

        var text = filePathOrName.Replace('_', ' ').Replace('-', ' ');

        // Check levels in priority order
        if (ReCm1.IsMatch(text)) return "CM1";
        if (ReCm2.IsMatch(text)) return "CM2";
        if (ReCe1.IsMatch(text)) return "CE1";
        if (ReCe2.IsMatch(text)) return "CE2";
        if (ReCp.IsMatch(text)) return "CP";
        if (Re6e.IsMatch(text)) return "6e";
        if (Re5e.IsMatch(text)) return "5e";
        if (Re4e.IsMatch(text)) return "4e";
        if (Re3e.IsMatch(text)) return "3e";
        if (ReGs.IsMatch(text)) return "GS";
        if (ReMs.IsMatch(text)) return "MS";
        if (RePs.IsMatch(text)) return "PS";

        return null;
    }

    public static string DetectDocumentType(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "Guide";

        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("guide") || lower.Contains("pédagogique") || lower.Contains("pedagogique"))
            return "Guide";
        if (lower.Contains("manuel") || lower.Contains("livre"))
            return "Manuel";
        if (lower.Contains("cahier") || lower.Contains("activité") || lower.Contains("activite"))
            return "Cahier";
        if (lower.Contains("progression") || lower.Contains("programme"))
            return "Progression";

        return "Guide";
    }

    public static string GenerateDefaultDropdownLabel(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var docType = DetectDocumentType(fileName);
        var level = DetectLevel(filePath) ?? string.Empty;

        // Try to extract extra info (e.g. subject or book title)
        var extra = ExtractExtraSubject(fileName, level);

        if (string.IsNullOrWhiteSpace(level) && string.IsNullOrWhiteSpace(extra))
            return fileName;

        if (string.IsNullOrWhiteSpace(extra))
            return $"{docType}-{level}";

        if (string.IsNullOrWhiteSpace(level))
            return $"{docType}-{extra}";

        return $"{docType}-{level}-{extra}";
    }

    private static string ExtractExtraSubject(string fileName, string detectedLevel)
    {
        var clean = fileName;
        // Strip common prefixes
        clean = Regex.Replace(clean, @"^(?:GUIDE|MANUEL|CAHIER)[_\s\-]*", "", RegexOptions.IgnoreCase);
        // Strip detected level
        if (!string.IsNullOrWhiteSpace(detectedLevel))
        {
            clean = Regex.Replace(clean, $@"\b{Regex.Escape(detectedLevel)}\b", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\b(?:6[eE]|5[eE]|4[eE]|3[eE]|1AC|2AC|3AC)\b", "", RegexOptions.IgnoreCase);
        }

        clean = clean.Replace('_', ' ').Replace('-', ' ');
        clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();

        return clean;
    }
}

