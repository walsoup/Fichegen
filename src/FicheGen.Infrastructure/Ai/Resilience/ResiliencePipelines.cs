using System.Net;
using System.Net.Sockets;
using FicheGen.Core.Ai;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace FicheGen.Infrastructure.Ai.Resilience;

public static class ResiliencePipelines
{
    public static ResiliencePipeline CreateNonStreamingPipeline(TimeSpan? attemptTimeout = null, TimeSpan? totalTimeout = null)
    {
        var perAttemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(60);
        var overallTimeout = totalTimeout ?? TimeSpan.FromSeconds(150);

        return new ResiliencePipelineBuilder()
            .AddTimeout(overallTimeout)
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ShouldRetryException),
                DelayGenerator = args =>
                {
                    if (args.Outcome.Exception is LlmException llmEx && llmEx.RetryAfter.HasValue)
                    {
                        return ValueTask.FromResult<TimeSpan?>(llmEx.RetryAfter.Value);
                    }
                    return ValueTask.FromResult<TimeSpan?>(null);
                }
            })
            .AddTimeout(perAttemptTimeout)
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                SamplingDuration = TimeSpan.FromSeconds(60),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ShouldRetryException)
            })
            .Build();
    }

    public static ResiliencePipeline CreateStreamingPipeline(TimeSpan? idleTimeout = null)
    {
        var watchdogTimeout = idleTimeout ?? TimeSpan.FromSeconds(60);

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ShouldRetryException),
                DelayGenerator = args =>
                {
                    if (args.Outcome.Exception is LlmException llmEx && llmEx.RetryAfter.HasValue)
                    {
                        return ValueTask.FromResult<TimeSpan?>(llmEx.RetryAfter.Value);
                    }
                    return ValueTask.FromResult<TimeSpan?>(null);
                }
            })
            .AddTimeout(watchdogTimeout)
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                SamplingDuration = TimeSpan.FromSeconds(60),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ShouldRetryException)
            })
            .Build();
    }

    public static bool ShouldRetryException(Exception ex)
    {
        if (ex is OperationCanceledException)
            return false;

        if (ex is LlmException llmEx)
        {
            if (llmEx.Kind == LlmExceptionKind.Auth || llmEx.Kind == LlmExceptionKind.Configuration || llmEx.Kind == LlmExceptionKind.Cancelled)
                return false;

            if (llmEx.StatusCode.HasValue)
            {
                var status = llmEx.StatusCode.Value;
                if (status == 400 || status == 401 || status == 403)
                    return false;
                if (status == 408 || status == 429 || status >= 500)
                    return true;
            }
            return llmEx.Kind == LlmExceptionKind.Network || llmEx.Kind == LlmExceptionKind.RateLimited || llmEx.Kind == LlmExceptionKind.Provider;
        }

        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                if (code == 400 || code == 401 || code == 403)
                    return false;
                if (code == 408 || code == 429 || code >= 500)
                    return true;
            }
            return true;
        }

        if (ex is SocketException || ex is TimeoutRejectedException)
            return true;

        return false;
    }
}
