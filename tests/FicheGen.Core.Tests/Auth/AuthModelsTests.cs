using System.Text.Json;
using FicheGen.Core.Auth;
using FluentAssertions;
using Xunit;

namespace FicheGen.Core.Tests.Auth;

public class AuthModelsTests
{
    [Fact]
    public void UserProfile_SerializationRoundtrip_PreservesAllFields()
    {
        var profile = new UserProfile(
            Id: "usr_12345",
            Email: "prof@education.gouv.fr",
            Nom: "Dupont",
            Prenom: "Jean",
            Civilite: "M.",
            DateDeNaissance: new DateTimeOffset(1985, 4, 12, 0, 0, 0, TimeSpan.Zero),
            SchoolName: "Collège Victor Hugo",
            Academie: "Paris",
            Discipline: "Mathématiques",
            DisplayName: "M. Jean Dupont",
            IsApproved: true,
            EmailConfirmed: true,
            Role: "teacher",
            MonthlyQuotaRemaining: 120,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<UserProfile>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(profile.Id);
        deserialized.Email.Should().Be(profile.Email);
        deserialized.Nom.Should().Be(profile.Nom);
        deserialized.Prenom.Should().Be(profile.Prenom);
        deserialized.Civilite.Should().Be(profile.Civilite);
        deserialized.SchoolName.Should().Be(profile.SchoolName);
        deserialized.Academie.Should().Be(profile.Academie);
        deserialized.Discipline.Should().Be(profile.Discipline);
        deserialized.DisplayName.Should().Be(profile.DisplayName);
        deserialized.IsApproved.Should().BeTrue();
        deserialized.EmailConfirmed.Should().BeTrue();
        deserialized.MonthlyQuotaRemaining.Should().Be(120);
    }

    [Fact]
    public void TeacherRegistrationRequest_ValidationAndIntegrity()
    {
        var request = new TeacherRegistrationRequest(
            Email: "marie.curie@ecole.fr",
            Password: "SuperSecretPassword123!",
            Nom: "Curie",
            Prenom: "Marie",
            Civilite: "Mme",
            DateDeNaissance: new DateTimeOffset(1980, 11, 7, 0, 0, 0, TimeSpan.Zero),
            SchoolName: "École Primaire Lavoisier",
            Academie: "Versailles",
            Discipline: "Sciences et technologie"
        );

        request.Email.Should().Be("marie.curie@ecole.fr");
        request.Nom.Should().Be("Curie");
        request.Prenom.Should().Be("Marie");
        request.Civilite.Should().Be("Mme");
        request.SchoolName.Should().Be("École Primaire Lavoisier");
        request.Academie.Should().Be("Versailles");
        request.Discipline.Should().Be("Sciences et technologie");
    }

    [Fact]
    public void AuthSession_CalculatesExpirationAndTokens()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(2);
        var session = new AuthSession(
            AccessToken: "jwt_token_sample",
            RefreshToken: "refresh_token_sample",
            ExpiresAt: expiresAt,
            User: new UserProfile("id_999", "test@domain.com")
        );

        session.AccessToken.Should().Be("jwt_token_sample");
        session.RefreshToken.Should().Be("refresh_token_sample");
        session.ExpiresAt.Should().Be(expiresAt);
        session.User.Email.Should().Be("test@domain.com");
    }
}
