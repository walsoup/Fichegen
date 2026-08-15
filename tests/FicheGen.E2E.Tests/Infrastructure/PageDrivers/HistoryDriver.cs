// ============================================================================
//  FicheGen.E2E.Tests — HistoryDriver
//  Pilote pour la vue d'historique et de recherche FTS5.
// ============================================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using FicheGen.App.ViewModels;
using FicheGen.Core.Storage;

namespace FicheGen.E2E.Tests.Infrastructure.PageDrivers;

public sealed class HistoryDriver
{
    private readonly TestEnvironment _env;

    public HistoryDriver(TestEnvironment env)
    {
        _env = env;
    }

    public async Task SearchAsync(string query)
    {
        _env.HistoryViewModel.SearchQuery = query;
        await _env.HistoryViewModel.LoadHistoryAsync();
    }

    public IReadOnlyList<HistoryItemViewModel> GetDisplayedItems()
    {
        return _env.HistoryViewModel.Items;
    }

    public async Task SelectItemAsync(HistoryItemViewModel item)
    {
        _env.HistoryViewModel.SelectedItem = item;
        await Task.CompletedTask;
    }

    public async Task DeleteSelectedItemAsync()
    {
        if (_env.HistoryViewModel.SelectedItem is { } item)
        {
            await _env.HistoryRepository.DeleteAsync(item.Id);
            await _env.HistoryViewModel.LoadHistoryAsync();
        }
    }
}
