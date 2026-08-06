namespace FicheGen.Core.Abstractions;

public interface ICredentialStore
{
    void Set(string key, string secret);
    string? Get(string key);
    void Remove(string key);
}
