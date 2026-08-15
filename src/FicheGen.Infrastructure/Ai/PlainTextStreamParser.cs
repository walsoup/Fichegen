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
        var decoder = Encoding.UTF8.GetDecoder();
        var charBuffer = new char[4096];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            if (ct.IsCancellationRequested)
                yield break;

            var charCount = decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0, flush: false);
            if (charCount > 0)
            {
                yield return new string(charBuffer, 0, charCount);
            }
        }

        // Flush any remaining characters
        var remainingChars = decoder.GetChars(Array.Empty<byte>(), 0, 0, charBuffer, 0, flush: true);
        if (remainingChars > 0)
        {
            yield return new string(charBuffer, 0, remainingChars);
        }
    }
}
