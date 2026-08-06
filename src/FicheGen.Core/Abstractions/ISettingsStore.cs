namespace FicheGen.Core.Abstractions;

public interface ISettingsStore
{
    T GetSettings<T>() where T : class, new();
    Task SaveSettingsAsync<T>(T settings, CancellationToken ct = default) where T : class;
}
