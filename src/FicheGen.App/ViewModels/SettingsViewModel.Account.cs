using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.Core.Auth;
using Microsoft.UI.Dispatching;

namespace FicheGen.App.ViewModels;

public partial class SettingsViewModel
{
    private readonly IAuthService? _authService;

    // ─────────────── Compte Enseignant & Quotas ───────────────

    [ObservableProperty] public partial bool IsAccountConnected { get; set; }
    [ObservableProperty] public partial string AccountDisplayName { get; set; } = "Enseignant·e";
    [ObservableProperty] public partial string AccountEmail { get; set; } = string.Empty;
    [ObservableProperty] public partial string AccountSchoolName { get; set; } = "Établissement scolaire";
    [ObservableProperty] public partial string AccountAcademie { get; set; } = string.Empty;
    [ObservableProperty] public partial string AccountDiscipline { get; set; } = string.Empty;
    [ObservableProperty] public partial int AccountQuotaRemaining { get; set; } = 150;
    [ObservableProperty] public partial bool AccountIsApproved { get; set; } = true;
    [ObservableProperty] public partial string AccountStatusBadge { get; set; } = "Mode Local / Hors-ligne";
    [ObservableProperty] public partial string AccountQuotaText { get; set; } = "150 crédits";
    [ObservableProperty] public partial string AccountDescription { get; set; } = "Gérez votre connexion, synchronisation cloud et accès aux modèles IA.";
    [ObservableProperty] public partial string AccountButtonText { get; set; } = "Connexion / Créer un compte…";

    private void InitializeAccountState()
    {
        if (_authService != null)
        {
            _authService.AuthStateChanged += (s, user) =>
            {
                var queue = DispatcherQueue.GetForCurrentThread();
                if (queue != null)
                {
                    queue.TryEnqueue(() => UpdateAccountProperties(user));
                }
                else
                {
                    UpdateAccountProperties(user);
                }
            };
        }
        UpdateAccountProperties(_authService?.CurrentUser);
    }

    public void UpdateAccountProperties(UserProfile? user)
    {
        if (user != null)
        {
            IsAccountConnected = true;
            AccountDisplayName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : (user.Email.Split('@')[0]);
            AccountEmail = user.Email;
            AccountSchoolName = !string.IsNullOrWhiteSpace(user.SchoolName) ? user.SchoolName : "Établissement scolaire";
            AccountAcademie = user.Academie ?? string.Empty;
            AccountDiscipline = user.Discipline ?? string.Empty;
            AccountQuotaRemaining = user.MonthlyQuotaRemaining;
            AccountIsApproved = user.IsApproved;
            AccountStatusBadge = user.IsApproved ? "Compte Enseignant Validé" : "En attente de validation";
            AccountQuotaText = $"{user.MonthlyQuotaRemaining} crédits IA restants ce mois-ci";
            AccountDescription = $"{AccountDisplayName} ({AccountEmail}) · {AccountSchoolName}";
            AccountButtonText = "Mon profil & Quota…";

            if (!string.IsNullOrWhiteSpace(user.DisplayName) && (TeacherName == "Enseignant·e" || string.IsNullOrWhiteSpace(TeacherName)))
            {
                TeacherName = user.DisplayName;
            }
            if (!string.IsNullOrWhiteSpace(user.SchoolName) && (SchoolName == "École / Établissement" || string.IsNullOrWhiteSpace(SchoolName)))
            {
                SchoolName = user.SchoolName;
            }
        }
        else
        {
            IsAccountConnected = false;
            AccountDisplayName = "Non connecté";
            AccountEmail = string.Empty;
            AccountSchoolName = string.Empty;
            AccountAcademie = string.Empty;
            AccountDiscipline = string.Empty;
            AccountQuotaRemaining = 0;
            AccountIsApproved = false;
            AccountStatusBadge = "Mode Local / Hors-ligne";
            AccountQuotaText = "Modèles Cloud inactifs sans compte";
            AccountDescription = "Connectez-vous avec votre compte enseignant pour activer les modèles IA Cloud et la synchronisation.";
            AccountButtonText = "Connexion / Créer un compte…";
        }
    }

    [RelayCommand]
    public async Task SignOutAccountAsync()
    {
        if (_authService != null)
        {
            await _authService.SignOutAsync();
            UpdateAccountProperties(null);
            StatusMessage = "Déconnexion réussie.";
        }
    }
}
