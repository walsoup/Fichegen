using Windows.Security.Credentials;
using FicheGen.Core.Abstractions;

namespace FicheGen.Infrastructure.Security;

public sealed class CredentialLockerStore : ICredentialStore
{
    private const string ResourceName = "FicheGen";
    private readonly DpapiCredentialStore _dpapiFallback = new();

    public void Set(string key, string secret)
    {
        try
        {
            var vault = new PasswordVault();
            try
            {
                var existing = vault.Retrieve(ResourceName, key);
                vault.Remove(existing);
            }
            catch
            {
                // Key not found in vault, ignore
            }

            vault.Add(new PasswordCredential(ResourceName, key, secret));
        }
        catch
        {
            // PasswordVault unavailable or restricted -> fallback to DPAPI
            _dpapiFallback.Set(key, secret);
        }
    }

    public string? Get(string key)
    {
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(ResourceName, key);
            cred.RetrievePassword();
            return cred.Password;
        }
        catch
        {
            return _dpapiFallback.Get(key);
        }
    }

    public void Remove(string key)
    {
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(ResourceName, key);
            vault.Remove(cred);
        }
        catch
        {
            // Ignore vault errors
        }

        _dpapiFallback.Remove(key);
    }
}
