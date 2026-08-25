using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth.OAuth2;

namespace FicheGen.Infrastructure.Vertex;

public sealed class VertexTokenProvider
{
    private sealed record CachedToken(string Token, DateTimeOffset Expiry);

    // Keyed by credential fingerprint so switching service accounts/projects
    // never reuses a token minted from a different credential.
    private readonly Dictionary<string, CachedToken> _tokenCache = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async ValueTask<string?> GetTokenAsync(string credentialJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credentialJson))
            return null;

        var credentialKey = ComputeCredentialKey(credentialJson);

        lock (_tokenCache)
        {
            if (_tokenCache.TryGetValue(credentialKey, out var cached) && DateTimeOffset.UtcNow < cached.Expiry)
            {
                return cached.Token;
            }
        }

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_tokenCache)
            {
                if (_tokenCache.TryGetValue(credentialKey, out var cached) && DateTimeOffset.UtcNow < cached.Expiry)
                {
                    return cached.Token;
                }
            }

            GoogleCredential credential;
            if (File.Exists(credentialJson))
            {
                credential = GoogleCredential.FromFile(credentialJson);
            }
            else
            {
                credential = GoogleCredential.FromJson(credentialJson);
            }

            credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct).ConfigureAwait(false);

            lock (_tokenCache)
            {
                _tokenCache[credentialKey] = new CachedToken(token, DateTimeOffset.UtcNow.AddMinutes(50));
            }
            return token;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Returns a stable, non-reversible fingerprint of the credential so the
    /// raw service-account JSON is never retained as cache state.</summary>
    private static string ComputeCredentialKey(string credentialJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(credentialJson));
        return Convert.ToHexString(hash);
    }
}
