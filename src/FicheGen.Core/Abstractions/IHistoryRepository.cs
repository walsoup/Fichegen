using FicheGen.Core.Storage;

namespace FicheGen.Core.Abstractions;

public interface IHistoryRepository
{
    Task InitializeAsync(CancellationToken ct = default);
    Task SaveAsync(HistoryItem item, CancellationToken ct = default);
    Task<HistoryItem?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<HistoryItem>> SearchAsync(
        string? query = null,
        string? typeFilter = null,
        bool isFavoriteOnly = false,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task RenameAsync(string id, string newTitle, CancellationToken ct = default);
    Task ToggleFavoriteAsync(string id, bool isFavorite, CancellationToken ct = default);
    Task<int> CleanupRetentionAsync(int retentionDays, CancellationToken ct = default);
}
