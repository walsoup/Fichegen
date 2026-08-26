using System;

namespace FicheGen.Core.Auth;

/// <summary>
/// Session d'authentification active (jetons stockés dans le coffre d'identification Windows).
/// </summary>
public sealed record AuthSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    UserProfile User);
