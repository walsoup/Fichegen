using Google.Apis.Auth.OAuth2;

namespace FicheGen.Infrastructure.Vertex;

public sealed class VertexTokenProvider
{
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async ValueTask<string?> GetTokenAsync(string credentialJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credentialJson))
            return null;

        if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
        {
            return _cachedToken;
        }

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
            {
                return _cachedToken;
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

            _cachedToken = token;
            _tokenExpiry = DateTimeOffset.UtcNow.AddMinutes(50);
            return _cachedToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
