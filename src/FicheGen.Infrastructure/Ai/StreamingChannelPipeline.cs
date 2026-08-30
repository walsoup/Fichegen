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

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Captured once: the consumer's finally disposes the CTS while the
        // producer may still be unwinding, and reading `.Token` on a disposed
        // CTS throws ObjectDisposedException.
        var producerToken = linkedCts.Token;

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in inputStream.WithCancellation(producerToken).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(item, producerToken).ConfigureAwait(false);
                }
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                // TryComplete is safe even if the consumer already completed
                // the writer (Complete would throw InvalidOperationException
                // and escape as an unobserved task exception).
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        try
        {
            var reader = channel.Reader;
            var batchBuilder = new System.Text.StringBuilder();
            var lastFlush = DateTime.UtcNow;

            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                // Flush on the interval or whenever reader has items
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

                if (batchBuilder.Length > 0 && DateTime.UtcNow - lastFlush >= flushInterval)
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
        finally
        {
            // If the consumer abandons iteration (break/early return/disposal),
            // stop the producer instead of letting it drain the HTTP response.
            linkedCts.Cancel();
            channel.Writer.TryComplete();
            // Dispose only after the producer has fully exited: it still reads
            // the token while unwinding, and racing Dispose throws.
            _ = producer.ContinueWith(_ => linkedCts.Dispose(), TaskScheduler.Default);
        }
    }
}
