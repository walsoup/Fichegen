using System.Runtime.CompilerServices;
using System.Text;

namespace FicheGen.Infrastructure.Ai;

public static class PlainTextStreamParser
{
    public static async IAsyncEnumerable<string> ReadTextChunksAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[4096];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            if (ct.IsCancellationRequested)
                yield break;

            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }
}
