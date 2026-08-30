using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.Core.Storage;
using FicheGen.Infrastructure.Auth;
using FicheGen.Infrastructure.Security;
using FicheGen.Infrastructure.Storage;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class SupabaseAuthServiceTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly string _testDir;
    private readonly DpapiCredentialStore _credStore;
    private readonly SettingsStore _settingsStore;
    private readonly HttpClient _httpClient;

    public SupabaseAuthServiceTests()
    {
        _server = WireMockServer.Start();
        _testDir = Path.Combine(Path.GetTempPath(), $"fichegen_auth_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _credStore = new DpapiCredentialStore(Path.Combine(_testDir, "credentials"));

        var settingsPath = Path.Combine(_testDir, "settings.json");
        _settingsStore = new SettingsStore(_credStore, settingsPath);

        var settings = new AppSettings();
        settings.Ai.Models["supabase.url"] = _server.Url!;
        settings.Ai.Models["supabase.anonKey"] = "test-anon-key";
        _settingsStore.SaveSettingsAsync(settings).GetAwaiter().GetResult();

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        _httpClient.Dispose();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SignInAsync_ValidCredentials_ReturnsSuccessAndStoresTokens()
    {
        // Arrange
        var fakeResponse = new
        {
            access_token = "mock_access_jwt_123",
            refresh_token = "mock_refresh_token_456",
            expires_in = 3600,
            user = new
            {
                id = "usr-12345",
                email = "prof@ecole.fr",
                user_metadata = new
                {
                    display_name = "M. Martin",
                    school_name = "École Pasteur"
                }
            }
        };

        _server.Given(Request.Create()
            .WithPath("/auth/v1/token")
            .WithParam("grant_type", "password")
            .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(fakeResponse)));

        var service = new SupabaseAuthService(_httpClient, _credStore, _settingsStore);

        // Act
        var result = await service.SignInAsync("prof@ecole.fr", "secretPassword123!");

        // Assert
        result.Success.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User!.DisplayName.Should().Be("M. Martin");
        result.User.SchoolName.Should().Be("École Pasteur");
        service.IsAuthenticated.Should().BeTrue();

        _credStore.Get("supabase_access_token").Should().Be("mock_access_jwt_123");
        _credStore.Get("supabase_refresh_token").Should().Be("mock_refresh_token_456");
        _credStore.Get("supabase_user_email").Should().Be("prof@ecole.fr");
    }

    [Fact]
    public async Task SignInAsync_InvalidCredentials_ReturnsFailure()
    {
        // Arrange
        _server.Given(Request.Create()
            .WithPath("/auth/v1/token")
            .WithParam("grant_type", "password")
            .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.BadRequest)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error_description\": \"Identifiants invalides\"}"));

        var service = new SupabaseAuthService(_httpClient, _credStore, _settingsStore);

        // Act
        var result = await service.SignInAsync("inconnu@ecole.fr", "mauvaisMotDePasse");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Identifiants invalides");
        service.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task SignOutAsync_ClearsCurrentUserAndCredentials()
    {
        // Arrange
        _credStore.Set("supabase_access_token", "active_token");
        _credStore.Set("supabase_user_email", "prof@ecole.fr");

        var service = new SupabaseAuthService(_httpClient, _credStore, _settingsStore);
        await service.RestoreSessionAsync();
        service.IsAuthenticated.Should().BeTrue();

        // Act
        await service.SignOutAsync();

        // Assert
        service.IsAuthenticated.Should().BeFalse();
        service.CurrentUser.Should().BeNull();
        _credStore.Get("supabase_access_token").Should().BeNull();
        _credStore.Get("supabase_user_email").Should().BeNull();
    }

    [Fact]
    public async Task SignUpAsync_WithFullProfile_RequiresEmailConfirmation_ReturnsSuccess()
    {
        // Arrange
        var fakeSignUpResponse = new
        {
            id = "usr-new-999",
            email = "nouveau.prof@ecole.fr",
            confirmation_sent_at = "2026-08-26T18:00:00Z"
        };

        _server.Given(Request.Create()
            .WithPath("/auth/v1/signup")
            .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(fakeSignUpResponse)));

        var service = new SupabaseAuthService(_httpClient, _credStore, _settingsStore);
        var req = new FicheGen.Core.Auth.TeacherRegistrationRequest(
            Email: "nouveau.prof@ecole.fr",
            Password: "password123!",
            Nom: "Dupont",
            Prenom: "Marie",
            Civilite: "Mme",
            DateDeNaissance: new DateTimeOffset(1992, 5, 14, 0, 0, 0, TimeSpan.Zero),
            SchoolName: "École Victor Hugo",
            Academie: "Académie de Paris",
            Discipline: "Professeur des écoles");

        // Act
        var result = await service.SignUpAsync(req);

        // Assert
        result.Success.Should().BeTrue();
        result.RequiresEmailConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task GetValidTokenAsync_WhenTokenExpiring_RefreshesTokenSuccessfully()
    {
        // Arrange
        // Create an expired JWT token (exp in the past)
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")).TrimEnd('=');
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"sub\":\"usr-123\",\"exp\":1000}")).TrimEnd('=');
        var expiredJwt = $"{header}.{payload}.signature";

        _credStore.Set("supabase_access_token", expiredJwt);
        _credStore.Set("supabase_refresh_token", "valid_refresh_token_789");
        _credStore.Set("supabase_user_email", "prof@ecole.fr");

        var refreshResponse = new
        {
            access_token = "new_refreshed_jwt_999",
            refresh_token = "new_refresh_token_888",
            expires_in = 3600
        };

        _server.Given(Request.Create()
            .WithPath("/auth/v1/token")
            .WithParam("grant_type", "refresh_token")
            .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(refreshResponse)));

        var service = new SupabaseAuthService(_httpClient, _credStore, _settingsStore);

        // Act
        var token = await service.GetValidTokenAsync();

        // Assert
        token.Should().Be("new_refreshed_jwt_999");
        _credStore.Get("supabase_access_token").Should().Be("new_refreshed_jwt_999");
        _credStore.Get("supabase_refresh_token").Should().Be("new_refresh_token_888");
    }

    [Fact]
    public async Task GetValidTokenAsync_WhenRefreshFails_ClearsSessionAndReturnsNull()
    {
        // Arrange
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")).TrimEnd('=');
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"sub\":\"usr-123\",\"exp\":1000}")).TrimEnd('=');
        var expiredJwt = $"{header}.{payload}.signature";

        _credStore.Set("supabase_access_token", expiredJwt);
        _credStore.Set("supabase_refresh_token", "invalid_refresh_token");
        _credStore.Set("supabase_user_email", "prof@ecole.fr");

        _server.Given(Request.Create()
            .WithPath("/auth/v1/token")
            .WithParam("grant_type", "refresh_token")
            .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.Unauthorized)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\": \"invalid_grant\"}"));

        var service = new SupabaseAuthService(_httpClient, _credStore, _settingsStore);

        // Act
        var token = await service.GetValidTokenAsync();

        // Assert
        token.Should().BeNull();
        service.IsAuthenticated.Should().BeFalse();
        _credStore.Get("supabase_access_token").Should().BeNull();
    }
}
