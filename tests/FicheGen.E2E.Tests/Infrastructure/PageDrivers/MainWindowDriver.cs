// ============================================================================
//  FicheGen.E2E.Tests — MainWindowDriver
//  Pilote de la fenêtre principale PROFstudio (Navigation, Barre de titre).
// ============================================================================

using System;
using FicheGen.App.ViewModels;

namespace FicheGen.E2E.Tests.Infrastructure.PageDrivers;

public sealed class MainWindowDriver
{
    private readonly TestEnvironment _env;

    public bool IsAssistantVisible { get; private set; } = false;
    public string SelectedTab { get; private set; } = "FichePage";
    public string CurrentStatusText => _env.FicheFormViewModel.StatusMessage;

    public MainWindowDriver(TestEnvironment env)
    {
        _env = env;
    }

    public void NavigateTo(string tabName)
    {
        SelectedTab = tabName;
    }

    public void ToggleAssistantVisibility(bool visible)
    {
        IsAssistantVisible = visible;
    }

    public void ExecuteGlobalSearch(string query)
    {
        _env.HistoryViewModel.SearchQuery = query;
    }
}
