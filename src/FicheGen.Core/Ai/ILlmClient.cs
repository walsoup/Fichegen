namespace FicheGen.Core.Ai;

public interface ILlmClient
{
    Task<string> GenerateAsync(LlmRequest req, AiRequestConfig cfg, CancellationToken ct);

    IAsyncEnumerable<string> GenerateStreamAsync(
        LlmRequest req,
        AiRequestConfig cfg,
        CancellationToken ct);
}
