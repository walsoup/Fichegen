using System.Runtime.CompilerServices;
using System.Text;
using FicheGen.Infrastructure.Ai;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests.Ai;

public class StreamingPipelineTests
{
    [Fact]
    public async Task ReadDataEventsAsync_ParsesStandardSseStream()
    {
        var sseData = @"
: keep-alive comment
data: {""chunk"": ""Hello ""}

data: {""chunk"": ""World""}

: another comment
data: [DONE]
";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sseData));
        var events = new List<string>();

        await foreach (var evt in SseStreamParser.ReadDataEventsAsync(stream, CancellationToken.None))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(3);
        events[0].Should().Be("{\"chunk\": \"Hello \"}");
        events[1].Should().Be("{\"chunk\": \"World\"}");
        events[2].Should().Be("[DONE]");
    }

    [Fact]
    public async Task ReadDataEventsAsync_MultiLineData_CombinesCorrectly()
    {
        var sseData = @"data: line 1
data: line 2

data: single line
";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sseData));
        var events = new List<string>();

        await foreach (var evt in SseStreamParser.ReadDataEventsAsync(stream, CancellationToken.None))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(2);
        events[0].Should().Be("line 1\nline 2");
        events[1].Should().Be("single line");
    }

    [Fact]
    public async Task ReadDataEventsAsync_CancellationToken_StopsImmediately()
    {
        var sseData = @"data: 1

data: 2

data: 3
";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sseData));
        using var cts = new CancellationTokenSource();

        var events = new List<string>();
        await foreach (var evt in SseStreamParser.ReadDataEventsAsync(stream, cts.Token))
        {
            events.Add(evt);
            cts.Cancel();
        }

        events.Should().HaveCount(1);
    }

    [Fact]
    public async Task BatchThrottledAsync_BatchesFastStreamItems()
    {
        async IAsyncEnumerable<string> GenerateFastStream([EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int i = 1; i <= 5; i++)
            {
                yield return $"item{i} ";
                await Task.Delay(5, ct);
            }
        }

        var batchedItems = new List<string>();
        await foreach (var batch in StreamingChannelPipeline.BatchThrottledAsync(
            GenerateFastStream(),
            TimeSpan.FromMilliseconds(40),
            CancellationToken.None))
        {
            batchedItems.Add(batch);
        }

        batchedItems.Should().NotBeEmpty();
        string.Concat(batchedItems).Should().Be("item1 item2 item3 item4 item5 ");
    }
}
