using System.Net;
using System.Net.Sockets;
using FicheGen.Core.Ai;
using FicheGen.Infrastructure.Ai.Resilience;
using FluentAssertions;
using Polly.Timeout;
using Xunit;

namespace FicheGen.Infrastructure.Tests.Ai;

public sealed class ResiliencePipelinesTests
{
    [Fact]
    public void ShouldRetryException_OperationCanceledException_ReturnsFalse()
    {
        var ex = new OperationCanceledException();
        ResiliencePipelines.ShouldRetryException(ex).Should().BeFalse();
    }

    [Theory]
    [InlineData(LlmExceptionKind.Auth, false)]
    [InlineData(LlmExceptionKind.Configuration, false)]
    [InlineData(LlmExceptionKind.Cancelled, false)]
    [InlineData(LlmExceptionKind.Network, true)]
    [InlineData(LlmExceptionKind.RateLimited, true)]
    [InlineData(LlmExceptionKind.Provider, true)]
    public void ShouldRetryException_LlmExceptionKinds_ClassifiesCorrectly(LlmExceptionKind kind, bool expected)
    {
        var ex = new LlmException(kind, "Test error");
        ResiliencePipelines.ShouldRetryException(ex).Should().Be(expected);
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    public void ShouldRetryException_LlmExceptionWithStatusCode_ClassifiesCorrectly(int statusCode, bool expected)
    {
        var ex = new LlmException(LlmExceptionKind.Provider, "Test status", statusCode);
        ResiliencePipelines.ShouldRetryException(ex).Should().Be(expected);
    }

    [Fact]
    public void ShouldRetryException_SocketException_ReturnsTrue()
    {
        var ex = new SocketException((int)SocketError.ConnectionReset);
        ResiliencePipelines.ShouldRetryException(ex).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryException_TimeoutRejectedException_ReturnsTrue()
    {
        var ex = new TimeoutRejectedException();
        ResiliencePipelines.ShouldRetryException(ex).Should().BeTrue();
    }

    [Fact]
    public async Task NonStreamingPipeline_RetriesOnTransientFailure_AndSucceeds()
    {
        var pipeline = ResiliencePipelines.CreateNonStreamingPipeline(
            attemptTimeout: TimeSpan.FromSeconds(5),
            totalTimeout: TimeSpan.FromSeconds(10));

        int attempts = 0;
        var result = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new LlmException(LlmExceptionKind.RateLimited, "Temporary 429", 429);
            }
            return await Task.FromResult("Success");
        });

        result.Should().Be("Success");
        attempts.Should().Be(2);
    }
}
