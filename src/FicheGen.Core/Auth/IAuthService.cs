using System;
using System.Threading;
using System.Threading.Tasks;

namespace FicheGen.Core.Auth;

public sealed record AuthResult(
    bool Success,
    string? ErrorMessage = null,
    UserProfile? User = null,
    string? AccessToken = null,
    bool RequiresEmailConfirmation = false);

public interface IAuthService
{
    bool IsAuthenticated { get; }
    UserProfile? CurrentUser { get; }
    event EventHandler<UserProfile?>? AuthStateChanged;

    Task<AuthResult> SignInAsync(string email, string password, CancellationToken ct = default);
    Task<AuthResult> SignUpAsync(TeacherRegistrationRequest request, CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
    Task<string?> GetValidTokenAsync(CancellationToken ct = default);
    Task<UserProfile?> RefreshProfileAsync(CancellationToken ct = default);
    Task RestoreSessionAsync(CancellationToken ct = default);
}
