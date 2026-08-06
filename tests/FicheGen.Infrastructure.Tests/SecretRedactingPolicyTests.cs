using FicheGen.Infrastructure.Diagnostics;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class SecretRedactingPolicyTests
{
    [Fact]
    public void RedactText_RedactsBearerTokenAndApiKey()
    {
        var input = "Headers: Authorization: Bearer secret_token_12345, x-goog-api-key: AIzaSyD1234567890123456789012345678901";
        var redacted = SecretRedactingPolicy.RedactText(input);

        Assert.DoesNotContain("secret_token_12345", redacted);
        Assert.DoesNotContain("AIzaSyD1234567890123456789012345678901", redacted);
        Assert.Contains("Authorization: Bearer [REDACTED]", redacted);
        Assert.Contains("x-goog-api-key: [REDACTED]", redacted);
    }
}
