using System;

namespace FicheGen.Core.Auth;

/// <summary>
/// Données d'inscription d'un enseignant.
/// </summary>
public sealed record TeacherRegistrationRequest(
    string Email,
    string Password,
    string Nom,
    string Prenom,
    string Civilite,
    DateTimeOffset? DateDeNaissance,
    string SchoolName,
    string Academie,
    string Discipline);
