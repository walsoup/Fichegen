using System.Security.Cryptography;
using System.Text;
using FicheGen.Core.Abstractions;

namespace FicheGen.Infrastructure.Security;

public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FicheGen.Entropy.v1");

    public void Set(string key, string secret)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(secretBytes, Entropy, DataProtectionScope.CurrentUser);
        var base64 = Convert.ToBase64String(protectedBytes);
        var storageFile = GetStoragePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(storageFile)!);
        File.WriteAllText(storageFile, base64);
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

    private static string GetStoragePath(string key)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen",
            "credentials",
            $"{key}.dpapi");
    }
}
