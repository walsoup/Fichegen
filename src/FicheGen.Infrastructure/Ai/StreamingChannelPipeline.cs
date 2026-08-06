using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace FicheGen.Infrastructure.Ai;

public static class StreamingChannelPipeline
{
    public static async IAsyncEnumerable<string> BatchThrottledAsync(
        IAsyncEnumerable<string> inputStream,
        TimeSpan flushInterval,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in inputStream.WithCancellation(ct).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
                }
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, ct);

        var reader = channel.Reader;
        var batchBuilder = new System.Text.StringBuilder();
        var lastFlush = DateTime.UtcNow;

        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                batchBuilder.Append(item);
                var now = DateTime.UtcNow;
                if (now - lastFlush >= flushInterval)
                {
                    yield return batchBuilder.ToString();
                    batchBuilder.Clear();
                    lastFlush = now;
                }
            }

            if (batchBuilder.Length > 0)
            {
                yield return batchBuilder.ToString();
                batchBuilder.Clear();
                lastFlush = DateTime.UtcNow;
            }
        }

        if (batchBuilder.Length > 0)
        {
            yield return batchBuilder.ToString();
        }
    }
}
