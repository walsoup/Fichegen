using FicheGen.Infrastructure.Storage;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public sealed class FileDraftStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileDraftStore _store;

    public FileDraftStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "FicheGenTests_Drafts_" + Guid.NewGuid().ToString("N"));
        _store = new FileDraftStore(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { /* ignore cleanup errors */ }
    }

    [Fact]
    public async Task SaveDraftAsync_And_LoadDraftAsync_PersistsAndRestoresFields()
    {
        var fields = new Dictionary<string, string?>
        {
            { "ClassLevel", "CM2" },
            { "Subject", "Français" },
            { "Topic", "Le passé composé" },
            { "DurationMinutes", "45" }
        };

        await _store.SaveDraftAsync("fiche", fields);

        var loaded = await _store.LoadDraftAsync("fiche");

        loaded.Should().NotBeNull();
        loaded!["ClassLevel"].Should().Be("CM2");
        loaded["Subject"].Should().Be("Français");
        loaded["Topic"].Should().Be("Le passé composé");
        loaded["DurationMinutes"].Should().Be("45");
    }

    [Fact]
    public async Task ClearDraftAsync_DeletesPersistedDraft()
    {
        var fields = new Dictionary<string, string?>
        {
            { "Topic", "Les tables" }
        };

        await _store.SaveDraftAsync("quiz", fields);
        var loadedBefore = await _store.LoadDraftAsync("quiz");
        loadedBefore.Should().NotBeNull();

        await _store.ClearDraftAsync("quiz");
        var loadedAfter = await _store.LoadDraftAsync("quiz");
        loadedAfter.Should().BeNull();
    }

    [Fact]
    public async Task LoadDraftAsync_NonExistentDraft_ReturnsNull()
    {
        var loaded = await _store.LoadDraftAsync("nonexistent_key");
        loaded.Should().BeNull();
    }
}
