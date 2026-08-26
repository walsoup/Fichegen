using System;

namespace FicheGen.Core.Auth;

/// <summary>
/// Profil utilisateur enseignant immutable complet.
/// </summary>
public sealed record UserProfile(
    string Id,
    string Email,
    string? Nom = null,
    string? Prenom = null,
    string? Civilite = null,
    DateTimeOffset? DateDeNaissance = null,
    string? SchoolName = null,
    string? Academie = null,
    string? Discipline = null,
    string? DisplayName = null,
    bool IsApproved = true,
    bool EmailConfirmed = false,
    string Role = "teacher",
    int MonthlyQuotaRemaining = 150,
    DateTimeOffset? CreatedAt = null);
