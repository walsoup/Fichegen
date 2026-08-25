using System.Security.Cryptography;
using System.Text;
using FicheGen.Core.Abstractions;

namespace FicheGen.Infrastructure.Security;

public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FicheGen.Entropy.v1");

    private readonly string _credentialsDirectory;

    public DpapiCredentialStore(string? credentialsDirectory = null)
    {
        _credentialsDirectory = credentialsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen",
            "credentials");
    }

    public void Set(string key, string secret)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(secretBytes, Entropy, DataProtectionScope.CurrentUser);
        var base64 = Convert.ToBase64String(protectedBytes);
        var storageFile = GetStoragePath(key);
        Directory.CreateDirectory(_credentialsDirectory);

        // Atomic write with a unique temp name: concurrent Set calls for the
        // same key must never interleave on one shared ".tmp" file, or a
        // half-written blob can be promoted over a good credential.
        var tempPath = $"{storageFile}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, base64);
            File.Move(tempPath, storageFile, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }

    public string? Get(string key)
    {
        var storageFile = GetStoragePath(key);
        if (!File.Exists(storageFile)) return null;

        try
        {
            var base64 = File.ReadAllText(storageFile);
            var protectedBytes = Convert.FromBase64String(base64);
            var secretBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(secretBytes);
        }
        catch
        {
            try { File.Delete(storageFile); } catch { }
            return null;
        }
    }

    public void Remove(string key)
    {
        var storageFile = GetStoragePath(key);
        if (File.Exists(storageFile))
        {
            File.Delete(storageFile);
        }
    }

    private string GetStoragePath(string key)
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_credentialsDirectory, $"{safeKey}.dpapi");
    }
}
