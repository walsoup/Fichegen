using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Auth;
using FicheGen.Core.Storage;
using Serilog;

namespace FicheGen.Infrastructure.Auth;

/// <summary>
/// Implémentation du service d'authentification et de gestion d'accès des enseignants via l'API REST de Supabase.
/// </summary>
public sealed class SupabaseAuthService : IAuthService
{
    // URL et clé publique anonyme du projet Supabase PROFstudio
    private const string DefaultSupabaseUrl = "https://bbodlidtaosxeyeovixe.supabase.co";
    private const string DefaultAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImJib2RsaWR0YW9zeGV5ZW92aXhlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODc3NTcxODIsImV4cCI6MjEwMzMzMzE4Mn0.qFHCVxa_iBuOFl-hQ29MkTUTacTq7hk58B4cBbf_sME";

    private readonly HttpClient _httpClient;
    private readonly ICredentialStore _credentialStore;
    private readonly ISettingsStore _settingsStore;

    private UserProfile? _currentUser;
    private string? _cachedAccessToken;
    private string? _cachedRefreshToken;
    private DateTimeOffset _tokenExpiresAt;

    public bool IsAuthenticated => _currentUser is not null;
    public UserProfile? CurrentUser => _currentUser;
    public event EventHandler<UserProfile?>? AuthStateChanged;

    public SupabaseAuthService(
        HttpClient? httpClient,
        ICredentialStore credentialStore,
        ISettingsStore settingsStore)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _credentialStore = credentialStore;
        _settingsStore = settingsStore;
    }

    private string GetSupabaseUrl()
    {
        var settings = _settingsStore.GetSettings<AppSettings>();
        if (settings?.Ai?.Models != null && settings.Ai.Models.TryGetValue("supabase.url", out var url) && !string.IsNullOrWhiteSpace(url))
        {
            return url.TrimEnd('/');
        }
        return DefaultSupabaseUrl;
    }

    private string GetAnonKey()
    {
        var settings = _settingsStore.GetSettings<AppSettings>();
        if (settings?.Ai?.Models != null && settings.Ai.Models.TryGetValue("supabase.anonKey", out var key) && !string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }
        return DefaultAnonKey;
    }

    public async Task<AuthResult> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        try
        {
            var url = $"{GetSupabaseUrl()}/auth/v1/token?grant_type=password";
            var payload = JsonSerializer.Serialize(new
            {
                email = email.Trim(),
                password = password
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", GetAnonKey());

            var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                var errorMsg = ExtractErrorMessage(json) ?? $"Échec de connexion (code {(int)res.StatusCode}).";
                return new AuthResult(false, errorMsg);
            }

            var node = JsonNode.Parse(json);
            if (node == null) return new AuthResult(false, "Réponse d'authentification invalide.");

            var accessToken = node["access_token"]?.GetValue<string>();
            var refreshToken = node["refresh_token"]?.GetValue<string>();
            var expiresIn = node["expires_in"]?.GetValue<int>() ?? 3600;
            var userObj = node["user"];

            if (string.IsNullOrEmpty(accessToken) || userObj == null)
            {
                return new AuthResult(false, "Données de session incomplètes.");
            }

            var userId = userObj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
            var userEmail = userObj["email"]?.GetValue<string>() ?? email;
            var confirmedAt = userObj["email_confirmed_at"]?.GetValue<string>();
            var meta = userObj["user_metadata"] as JsonObject;
            var nom = meta?["nom"]?.GetValue<string>();
            var prenom = meta?["prenom"]?.GetValue<string>();
            var civilite = meta?["civilite"]?.GetValue<string>();
            var academie = meta?["academie"]?.GetValue<string>();
            var discipline = meta?["discipline"]?.GetValue<string>();
            var schoolName = meta?["school_name"]?.GetValue<string>() ?? "Établissement scolaire";
            var displayName = meta?["display_name"]?.GetValue<string>() ?? $"{civilite} {prenom} {nom}".Trim();
            if (string.IsNullOrWhiteSpace(displayName)) displayName = userEmail.Split('@')[0];

            var profile = new UserProfile(
                Id: userId,
                Email: userEmail,
                Nom: nom,
                Prenom: prenom,
                Civilite: civilite,
                SchoolName: schoolName,
                Academie: academie,
                Discipline: discipline,
                DisplayName: displayName,
                IsApproved: true,
                EmailConfirmed: !string.IsNullOrEmpty(confirmedAt),
                Role: "teacher",
                MonthlyQuotaRemaining: 150,
                CreatedAt: DateTimeOffset.UtcNow
            );

            _cachedAccessToken = accessToken;
            _cachedRefreshToken = refreshToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
            _currentUser = profile;

            // Sauvegarde sécurisée dans le coffre Windows
            SaveSessionToVault(accessToken, refreshToken, profile);

            SafeNotifyAuthStateChanged(_currentUser);
            return new AuthResult(true, null, profile, accessToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erreur lors de la connexion Supabase.");
            return new AuthResult(false, $"Erreur de connexion : {ex.Message}");
        }
    }

    public async Task<AuthResult> SignUpAsync(TeacherRegistrationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

        try
        {
            var url = $"{GetSupabaseUrl()}/auth/v1/signup";
            var displayName = $"{request.Civilite} {request.Prenom} {request.Nom}".Trim();
            var payload = JsonSerializer.Serialize(new
            {
                email = request.Email.Trim(),
                password = request.Password,
                data = new
                {
                    nom = request.Nom?.Trim() ?? string.Empty,
                    prenom = request.Prenom?.Trim() ?? string.Empty,
                    civilite = request.Civilite ?? "M.",
                    date_de_naissance = request.DateDeNaissance?.ToString("yyyy-MM-dd"),
                    school_name = request.SchoolName?.Trim() ?? string.Empty,
                    academie = request.Academie?.Trim() ?? string.Empty,
                    discipline = request.Discipline?.Trim() ?? string.Empty,
                    display_name = displayName
                }
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", GetAnonKey());

            var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                var errorMsg = ExtractErrorMessage(json) ?? $"Échec de l'inscription (code {(int)res.StatusCode}).";
                return new AuthResult(false, errorMsg);
            }

            var node = JsonNode.Parse(json);
            var sessionObj = node?["session"];
            var accessToken = sessionObj?["access_token"]?.GetValue<string>();

            // Si Supabase requiert la confirmation d'e-mail (session est null au signup)
            if (string.IsNullOrEmpty(accessToken))
            {
                return new AuthResult(
                    Success: true,
                    ErrorMessage: null,
                    User: null,
                    AccessToken: null,
                    RequiresEmailConfirmation: true);
            }

            // Si auto-confirm est activé, se connecter directement
            return await SignInAsync(request.Email, request.Password, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erreur lors de l'inscription Supabase.");
            return new AuthResult(false, $"Erreur d'inscription : {ex.Message}");
        }
    }

    public Task SignOutAsync(CancellationToken ct = default)
    {
        _currentUser = null;
        _cachedAccessToken = null;
        _cachedRefreshToken = null;

        try
        {
            _credentialStore.Remove("supabase_access_token");
            _credentialStore.Remove("supabase_refresh_token");
            _credentialStore.Remove("supabase_user_id");
            _credentialStore.Remove("supabase_user_email");
            _credentialStore.Remove("supabase_user_name");
            _credentialStore.Remove("supabase_user_school");
        }
        catch { }

        SafeNotifyAuthStateChanged(null);
        return Task.CompletedTask;
    }

    public async Task<string?> GetValidTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_cachedAccessToken))
        {
            await RestoreSessionAsync(ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(_cachedAccessToken)) return null;

        if (DateTimeOffset.UtcNow >= _tokenExpiresAt && !string.IsNullOrEmpty(_cachedRefreshToken))
        {
            var refreshed = await RefreshTokenAsync(_cachedRefreshToken, ct).ConfigureAwait(false);
            if (refreshed) return _cachedAccessToken;
        }

        return _cachedAccessToken;
    }

    public async Task<UserProfile?> RefreshProfileAsync(CancellationToken ct = default)
    {
        var token = await GetValidTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token)) return null;

        try
        {
            var url = $"{GetSupabaseUrl()}/auth/v1/user";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("apikey", GetAnonKey());
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return _currentUser;

            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json);
            if (node == null) return _currentUser;

            var meta = node["user_metadata"] as JsonObject;
            var displayName = meta?["display_name"]?.GetValue<string>() ?? _currentUser?.DisplayName ?? "Enseignant·e";
            var schoolName = meta?["school_name"]?.GetValue<string>() ?? _currentUser?.SchoolName ?? "École";

            _currentUser = new UserProfile(
                Id: node["id"]?.GetValue<string>() ?? _currentUser?.Id ?? "",
                Email: node["email"]?.GetValue<string>() ?? _currentUser?.Email ?? "",
                DisplayName: displayName,
                SchoolName: schoolName,
                IsApproved: true,
                Role: "teacher",
                MonthlyQuotaRemaining: 150,
                CreatedAt: _currentUser?.CreatedAt ?? DateTimeOffset.UtcNow
            );

            SafeNotifyAuthStateChanged(_currentUser);
            return _currentUser;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Rafraîchissement du profil utilisateur impossible.");
            return _currentUser;
        }
    }

    public Task RestoreSessionAsync(CancellationToken ct = default)
    {
        try
        {
            var token = _credentialStore.Get("supabase_access_token");
            var refresh = _credentialStore.Get("supabase_refresh_token");
            var userId = _credentialStore.Get("supabase_user_id");
            var email = _credentialStore.Get("supabase_user_email");
            var name = _credentialStore.Get("supabase_user_name");
            var school = _credentialStore.Get("supabase_user_school");

            if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(email))
            {
                _cachedAccessToken = token;
                _cachedRefreshToken = refresh;
                _tokenExpiresAt = DateTimeOffset.UtcNow.AddHours(2);

                _currentUser = new UserProfile(
                    Id: userId ?? Guid.NewGuid().ToString(),
                    Email: email,
                    DisplayName: name ?? email.Split('@')[0],
                    SchoolName: school ?? "École",
                    IsApproved: true,
                    Role: "teacher",
                    MonthlyQuotaRemaining: 150,
                    CreatedAt: DateTimeOffset.UtcNow
                );

                SafeNotifyAuthStateChanged(_currentUser);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Restauration de la session Supabase impossible.");
        }

        return Task.CompletedTask;
    }

    private async Task<bool> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var url = $"{GetSupabaseUrl()}/auth/v1/token?grant_type=refresh_token";
            var payload = JsonSerializer.Serialize(new { refresh_token = refreshToken });

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", GetAnonKey());

            var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return false;

            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json);
            if (node == null) return false;

            _cachedAccessToken = node["access_token"]?.GetValue<string>();
            _cachedRefreshToken = node["refresh_token"]?.GetValue<string>() ?? refreshToken;
            var expiresIn = node["expires_in"]?.GetValue<int>() ?? 3600;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);

            if (!string.IsNullOrEmpty(_cachedAccessToken))
            {
                _credentialStore.Set("supabase_access_token", _cachedAccessToken);
                if (!string.IsNullOrEmpty(_cachedRefreshToken))
                {
                    _credentialStore.Set("supabase_refresh_token", _cachedRefreshToken);
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Échec du rafraîchissement du jeton Supabase.");
        }

        return false;
    }

    private void SaveSessionToVault(string accessToken, string? refreshToken, UserProfile profile)
    {
        try
        {
            _credentialStore.Set("supabase_access_token", accessToken);
            if (!string.IsNullOrEmpty(refreshToken)) _credentialStore.Set("supabase_refresh_token", refreshToken);
            _credentialStore.Set("supabase_user_id", profile.Id);
            _credentialStore.Set("supabase_user_email", profile.Email);
            if (!string.IsNullOrEmpty(profile.DisplayName)) _credentialStore.Set("supabase_user_name", profile.DisplayName);
            if (!string.IsNullOrEmpty(profile.SchoolName)) _credentialStore.Set("supabase_user_school", profile.SchoolName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Enregistrement de la session dans le coffre Windows impossible.");
        }
    }

    private void SafeNotifyAuthStateChanged(UserProfile? profile)
    {
        if (AuthStateChanged == null) return;

        foreach (var handler in AuthStateChanged.GetInvocationList())
        {
            try
            {
                ((EventHandler<UserProfile?>)handler).Invoke(this, profile);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Erreur dans un gestionnaire d'événement AuthStateChanged.");
            }
        }
    }

    private static string? ExtractErrorMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node?["error_description"]?.GetValue<string>()
                ?? node?["msg"]?.GetValue<string>()
                ?? node?["message"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
}
