using FicheGen.Infrastructure.Diagnostics;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class SecretRedactingPolicyTests
{
    [Fact]
    public void RedactText_RedactsBearerTokenAndApiKey()
    {
        var input = "Headers: Authorization: Bearer secret_token_12345, x-goog-api-key: AIzaSyD1234567890123456789012345678901";
        var redacted = SecretRedactingPolicy.RedactText(input);

        redacted.Should().NotContain("secret_token_12345");
        redacted.Should().NotContain("AIzaSyD1234567890123456789012345678901");
        redacted.Should().Contain("Authorization: Bearer [REDACTED]");
        redacted.Should().Contain("x-goog-api-key: [REDACTED]");
    }

    [Theory]
    [InlineData("gemini_api_key")]
    [InlineData("openai_api_key")]
    [InlineData("anthropic_api_key")]
    [InlineData("proxy_api_key")]
    [InlineData("vercel_api_key")]
    public void RedactText_SnakeCaseStoreKeys_RedactsJsonValues(string keyName)
    {
        // Deliberately matches no value-pattern regex (no AIzaSy / sk- prefix):
        // only the JSON key-name rule can redact it, so this theory actually
        // guards the snake_case store-key path against regressions.
        var secret = "Zk9vQmFyQmF6cXV1eDU1NjY2Njc3ODg4OQ==";
        var input = $$"""{"{{keyName}}": "{{secret}}"}""";
        var redacted = SecretRedactingPolicy.RedactText(input);

        redacted.Should().NotContain(secret);
        redacted.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void RedactText_ServiceAccountPrivateKey_IsRedacted()
    {
        const string pem = "-----BEGIN PRIVATE KEY-----MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC7secretmaterial-----END PRIVATE KEY-----";
        var input = $$"""{"type":"service_account","private_key":"{{pem}}","client_email":"sa@project.iam.gserviceaccount.com"}""";
        var redacted = SecretRedactingPolicy.RedactText(input);

        redacted.Should().NotContain(pem);
        redacted.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void RedactText_ApiKeyQueryParameter_IsRedacted()
    {
        var input = "POST https://generativelanguage.googleapis.com/v1beta/models/gemini:generateContent?key=AIzaSyD1234567890123456789012345";
        var redacted = SecretRedactingPolicy.RedactText(input);

        redacted.Should().NotContain("AIzaSyD1234567890123456789012345");
        redacted.Should().Contain("key=[REDACTED]");
    }
}
