namespace FicheGen.Core.Abstractions;

/// <summary>
/// Contrat minimal de persistance des brouillons de formulaires.
/// </summary>
public interface IDraftStore
{
    Task SaveDraftAsync(string draftKey, IReadOnlyDictionary<string, string?> fields, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string?>?> LoadDraftAsync(string draftKey, CancellationToken cancellationToken = default);
    Task ClearDraftAsync(string draftKey, CancellationToken cancellationToken = default);
}
