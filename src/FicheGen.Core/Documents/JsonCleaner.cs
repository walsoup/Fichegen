using System.Text.Json;
using System.Text.RegularExpressions;

namespace FicheGen.Core.Documents;

public static class JsonCleaner
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = raw.Trim();

        // 1. Strip Markdown code fences
        var fenceMatch = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
        {
            text = fenceMatch.Groups[1].Value.Trim();
        }

        // 2. Extract outer JSON object if embedded in surrounding text
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            text = text.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        // 3. Fix trailing commas before closing braces/brackets
        text = Regex.Replace(text, @",\s*([\}\]])", "$1");

        return text;
    }

    public static bool TryDeserializeDocument(string raw, out GeneratedDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var cleaned = Clean(raw);
        if (string.IsNullOrWhiteSpace(cleaned))
            return false;

        try
        {
            document = JsonSerializer.Deserialize<GeneratedDocument>(cleaned, DefaultJsonOptions);
            if (document != null && document.Metadata != null && document.Blocks != null)
            {
                return true;
            }
        }
        catch (JsonException)
        {
            // Retry with outer object normalization if needed
        }

        return false;
    }
}
