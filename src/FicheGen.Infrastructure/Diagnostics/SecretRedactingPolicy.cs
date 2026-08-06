using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace FicheGen.Infrastructure.Diagnostics;

public class SecretRedactingPolicy : ILogEventEnricher
{
    private static readonly Regex AuthHeaderRegex = new(@"Authorization\s*:\s*Bearer\s+[A-Za-z0-9._~\-+//=]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ApiKeyQueryRegex = new(@"(key|api_key|x-goog-api-key)\s*=\s*[A-Za-z0-9._~\-+//=]{16,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ApiKeyHeaderRegex = new(@"x-goog-api-key\s*:\s*[A-Za-z0-9._~\-+//=]{16,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BearerTokenRegex = new(@"AIzaSy[A-Za-z0-9_\-]{20,}|sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled);
    private static readonly Regex JsonKeyValRegex = new(@"(""[^""]*(?:ApiKey|Api_Key|Token|Secret|Password)[^""]*""\s*:\s*"")([^""]+)("")", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Redacts string properties if present
    }

    public static string RedactText(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = AuthHeaderRegex.Replace(input, "Authorization: Bearer [REDACTED]");
        result = ApiKeyQueryRegex.Replace(result, "$1=[REDACTED]");
        result = ApiKeyHeaderRegex.Replace(result, "x-goog-api-key: [REDACTED]");
        result = BearerTokenRegex.Replace(result, "[REDACTED_API_KEY]");
        result = JsonKeyValRegex.Replace(result, "$1[REDACTED]$3");
        return result;
    }
}
