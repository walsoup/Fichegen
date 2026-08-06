namespace FicheGen.Core.Ai;

public enum LlmExceptionKind
{
    Auth,
    RateLimited,
    Network,
    Provider,
    InvalidResponse,
    Cancelled,
    Configuration
}

public class LlmException : Exception
{
    public LlmExceptionKind Kind { get; }
    public int? StatusCode { get; }
    public TimeSpan? RetryAfter { get; }

    public LlmException(LlmExceptionKind kind, string message, int? statusCode = null, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}
