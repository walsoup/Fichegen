using System.Collections.Immutable;
using System.IO;
using System.Text;
using FicheGen.Core.Ai;
using FicheGen.Core.Ai.Routing;
using FicheGen.Core.Documents;
using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Ai;
using FicheGen.Infrastructure.Storage;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class M1Phase1StressTests
{
    /// <summary>
    /// Custom Stream that yields bytes in small chunks to simulate UTF-8 multi-byte characters
    /// split across read buffer boundaries.
    /// </summary>
    private sealed class ChunkedStream : Stream
    {
        private readonly byte[][] _chunks;
        private int _chunkIndex;

        public ChunkedStream(byte[][] chunks)
        {
            _chunks = chunks;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _chunks.Sum(c => c.Length);
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_chunkIndex >= _chunks.Length) return 0;
            var currentChunk = _chunks[_chunkIndex++];
            Array.Copy(currentChunk, 0, buffer, offset, Math.Min(currentChunk.Length, count));
            return currentChunk.Length;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task PlainTextStreamParser_Utf8BoundarySplit_PreservesCharactersWithoutUnicodeReplacement()
    {
        // "Élève à l'école & 🌟"
        // 'É' = 0xC3 0x89
        // 'è' = 0xC3 0xA8
        // 'é' = 0xC3 0xA9
        // '🌟' = 0xF0 0x9F 0x8C 0x9F (4 bytes)

        // Split "Élève" across byte boundaries:
        // Chunk 1: [0xC3] (first byte of É)
        // Chunk 2: [0x89, 0x6C, 0xC3] ('É' 2nd byte, 'l', 'è' 1st byte)
        // Chunk 3: [0xA8, 0x76, 0xC3] ('è' 2nd byte, 'v', 'e' 1st byte 'è' or 'é')
        // Chunk 4: [0xA9] ('é' 2nd byte)
        byte[][] chunks = new byte[][]
        {
            new byte[] { 0xC3 },
            new byte[] { 0x89, (byte)'l', 0xC3 },
            new byte[] { 0xA8, (byte)'v', 0xC3 },
            new byte[] { 0xA9, (byte)' ', (byte)'&', (byte)' ', 0xF0, 0x9F },
            new byte[] { 0x8C, 0x9F }
        };

        using var stream = new ChunkedStream(chunks);
        var sb = new StringBuilder();

        await foreach (var chunk in PlainTextStreamParser.ReadTextChunksAsync(stream, CancellationToken.None))
        {
            sb.Append(chunk);
        }

        var result = sb.ToString();

        // Must NOT contain Unicode Replacement Character U+FFFD ()
        result.Should().NotContain("\uFFFD", "UTF-8 multi-byte characters split across chunk boundaries must be properly buffered and decoded");
        result.Should().Be("Élèvé & 🌟");
    }

    [Fact]
    public async Task StreamingChannelPipeline_WhenInputStreamThrows_CompletesChannelWithException()
    {
        static async IAsyncEnumerable<string> ThrowingStream()
        {
            yield return "chunk1";
            await Task.Yield();
            throw new InvalidOperationException("Stream error mid-flight");
        }

        var act = async () =>
        {
            await foreach (var batch in StreamingChannelPipeline.BatchThrottledAsync(ThrowingStream(), TimeSpan.FromMilliseconds(10), CancellationToken.None))
            {
                _ = batch;
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Stream error mid-flight");
    }
}
