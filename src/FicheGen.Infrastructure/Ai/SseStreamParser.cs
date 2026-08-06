using System.Runtime.CompilerServices;
using System.Text;

namespace FicheGen.Infrastructure.Ai;

public static class SseStreamParser
{
    public static async IAsyncEnumerable<string> ReadDataEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (ct.IsCancellationRequested)
                yield break;

            if (line.Length == 0) // Event boundary
            {
                if (data.Length > 0)
                {
                    var payload = data.ToString().TrimEnd('\r', '\n');
                    data.Clear();
                    if (!string.IsNullOrEmpty(payload))
                    {
                        yield return payload;
                    }
                }
                continue;
            }

            if (line[0] == ':') continue; // Keep-alive comment

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var content = line.Substring(5).TrimStart(' ');
                data.Append(content).Append('\n');
            }
        }

        if (data.Length > 0)
        {
            var payload = data.ToString().TrimEnd('\r', '\n');
            if (!string.IsNullOrEmpty(payload))
            {
                yield return payload;
            }
        }
    }
}
