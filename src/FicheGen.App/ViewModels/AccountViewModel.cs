using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Auth;
using FicheGen.Core.Storage;

namespace FicheGen.App.ViewModels;

public partial class AccountViewModel : ObservableObject, IDisposable
{
    private readonly IAuthService _authService;
    private readonly ISettingsStore _settingsStore;
    private readonly EventHandler<UserProfile?> _authStateChangedHandler;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotAuthenticated))]
    [NotifyPropertyChangedFor(nameof(AccountStatusBadge))]
    [NotifyPropertyChangedFor(nameof(AccountStatusColor))]
    public partial bool IsAuthenticated { get; set; }

    public bool IsNotAuthenticated => !IsAuthenticated;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignUpCommand))]
    public partial bool IsBusy { get; set; }

    public bool CanSubmit => !IsBusy;
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsSignUpMode { get; set; }
    [ObservableProperty] public partial bool EmailConfirmationSent { get; set; }

    // Champs du formulaire
    [ObservableProperty] public partial string Civilite { get; set; } = "Mme";
    [ObservableProperty] public partial string Nom { get; set; } = string.Empty;
    [ObservableProperty] public partial string Prenom { get; set; } = string.Empty;
    [ObservableProperty] public partial string SchoolName { get; set; } = string.Empty;
    [ObservableProperty] public partial string Academie { get; set; } = "Académie de Paris";
    [ObservableProperty] public partial string Discipline { get; set; } = "Professeur des écoles";
    [ObservableProperty] public partial string Email { get; set; } = string.Empty;
    [ObservableProperty] public partial string Password { get; set; } = string.Empty;
    [ObservableProperty] public partial string ConfirmPassword { get; set; } = string.Empty;

    public IReadOnlyList<string> Civilites { get; } = new[] { "Mme", "M.", "Autre" };
    public IReadOnlyList<string> Academies { get; } = new[]
    {
        "Académie de Paris", "Académie de Versailles", "Académie de Créteil",
        "Académie de Lyon", "Académie de Lille", "Académie de Marseille / Aix",
        "Académie de Bordeaux", "Académie de Toulouse", "Académie de Nantes",
        "Académie de Rennes", "Académie de Strasbourg", "Académie de Montpellier",
        "Académie de Grenoble", "Académie de Nice", "Autre / Étranger"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UserDisplayName))]
    [NotifyPropertyChangedFor(nameof(UserEmail))]
    [NotifyPropertyChangedFor(nameof(UserSchool))]
    [NotifyPropertyChangedFor(nameof(QuotaInfo))]
    public partial UserProfile? CurrentUser { get; set; }

    public string UserDisplayName => CurrentUser?.DisplayName ?? Services.L10n.Get("Profile_DefaultName", "Enseignant·e");
    public string UserEmail => CurrentUser?.Email ?? "compte-local@profstudio.app";
    public string UserSchool => CurrentUser?.SchoolName ?? Services.L10n.Get("Profile_DefaultSchool", "École / Établissement");
    public string QuotaInfo => CurrentUser != null ? Services.L10n.Format("Account_QuotaFormat", CurrentUser.MonthlyQuotaRemaining) : Services.L10n.Get("Account_UnlimitedQuota", "Mode local / Illimité");

    public string AccountStatusBadge => IsAuthenticated ? Services.L10n.Get("Account_ActiveBadge", "Compte Enseignant Actif") : Services.L10n.Get("Account_GuestMode", "Mode Invité (Local)");
    public string AccountStatusColor => IsAuthenticated ? "#059669" : "#64748B";

    public AccountViewModel(IAuthService authService, ISettingsStore settingsStore)
    {
        _authService = authService;
        _settingsStore = settingsStore;

        CurrentUser = _authService.CurrentUser;
        IsAuthenticated = _authService.IsAuthenticated;

        _authStateChangedHandler = (s, user) =>
        {
            var dispatcher = App.CurrentMainWindow?.DispatcherQueue;
            if (dispatcher != null && !dispatcher.HasThreadAccess)
            {
                dispatcher.TryEnqueue(() => ApplyUserUpdate(user));
            }
            else
            {
                ApplyUserUpdate(user);
            }
        };

        _authService.AuthStateChanged += _authStateChangedHandler;
    }

    public void Dispose()
    {
        _authService.AuthStateChanged -= _authStateChangedHandler;
    }

    private void ApplyUserUpdate(UserProfile? user)
    {
        CurrentUser = user;
        IsAuthenticated = user is not null;
        if (user != null)
        {
            Nom = user.Nom ?? string.Empty;
            Prenom = user.Prenom ?? string.Empty;
            SchoolName = user.SchoolName ?? string.Empty;
            Academie = user.Academie ?? "Académie de Paris";
            Discipline = user.Discipline ?? "Professeur des écoles";
        }
    }

    [RelayCommand]
    public void ToggleMode()
    {
        IsSignUpMode = !IsSignUpMode;
        EmailConfirmationSent = false;
        StatusMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    public async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            StatusMessage = Services.L10n.Get("Account_MissingEmailPassword", "Veuillez saisir votre adresse e-mail et votre mot de passe.");
            return;
        }

        IsBusy = true;
        StatusMessage = Services.L10n.Get("Account_SigningIn", "Connexion en cours…");

        try
        {
            var result = await _authService.SignInAsync(Email, Password);
            if (result.Success)
            {
                StatusMessage = Services.L10n.Get("Account_SignInSuccess", "✅ Connexion réussie !");
                Password = string.Empty;

                if (result.User != null)
                {
                    var settings = _settingsStore.GetSettings<AppSettings>();
                    if (!string.IsNullOrWhiteSpace(result.User.DisplayName)) settings.Defaults.TeacherName = result.User.DisplayName;
                    if (!string.IsNullOrWhiteSpace(result.User.SchoolName)) settings.Defaults.SchoolName = result.User.SchoolName;
                    await _settingsStore.SaveSettingsAsync(settings);
                }
            }
            else
            {
                StatusMessage = $"⚠ {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur : {Services.ErrorMessageTranslator.ToUserFriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    public async Task SignUpAsync()
    {
        if (string.IsNullOrWhiteSpace(Nom) || string.IsNullOrWhiteSpace(Prenom))
        {
            StatusMessage = Services.L10n.Get("Account_MissingName", "Veuillez renseigner votre nom et prénom.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SchoolName))
        {
            StatusMessage = Services.L10n.Get("Account_MissingSchool", "Veuillez renseigner votre établissement scolaire.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
        {
            StatusMessage = Services.L10n.Get("Account_InvalidEmail", "Veuillez renseigner une adresse e-mail valide.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Password) || Password.Length < 6)
        {
            StatusMessage = Services.L10n.Get("Account_PasswordTooShort", "Le mot de passe doit comporter au moins 6 caractères.");
            return;
        }

        if (Password != ConfirmPassword)
        {
            StatusMessage = Services.L10n.Get("Account_PasswordMismatch", "Les deux mots de passe ne correspondent pas.");
            return;
        }

        IsBusy = true;
        StatusMessage = Services.L10n.Get("Account_CreatingAccount", "Création et enregistrement de votre compte enseignant…");

        try
        {
            var req = new TeacherRegistrationRequest(
                Email: Email,
                Password: Password,
                Nom: Nom,
                Prenom: Prenom,
                Civilite: Civilite,
                DateDeNaissance: null,
                SchoolName: SchoolName,
                Academie: Academie,
                Discipline: Discipline);

            var result = await _authService.SignUpAsync(req);
            if (result.Success)
            {
                if (result.RequiresEmailConfirmation)
                {
                    EmailConfirmationSent = true;
                    StatusMessage = Services.L10n.Format("Account_ConfirmationSent", Email);
                }
                else
                {
                    StatusMessage = Services.L10n.Get("Account_SignUpSuccess", "✅ Compte enseignant créé et activé avec succès !");
                    Password = string.Empty;
                    ConfirmPassword = string.Empty;
                    IsSignUpMode = false;
                }
            }
            else
            {
                StatusMessage = $"⚠ {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur : {Services.ErrorMessageTranslator.ToUserFriendlyMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SignOutAsync()
    {
        IsBusy = true;
        try
        {
            await _authService.SignOutAsync();
            StatusMessage = Services.L10n.Get("Account_SignedOut", "Déconnecté.");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
