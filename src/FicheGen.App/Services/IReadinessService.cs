using System;
using System.Threading.Tasks;

namespace FicheGen.App.Services;

/// <summary>
/// États de préparation et de disponibilité opérationnelle de l'assistant IA (UX-05).
/// </summary>
public enum ReadinessState
{
    /// <summary>Démarrage / état initial avant vérification.</summary>
    Unknown,

    /// <summary>Aucune clé d'accès configurée dans le coffre pour le fournisseur actif.</summary>
    NotConfigured,

    /// <summary>Un test de connexion ou une validation est en cours.</summary>
    Checking,

    /// <summary>Fournisseur configuré, clé présente et connexion opérationnelle.</summary>
    Ready,

    /// <summary>Dernière tentative échouée avec une erreur temporaire/réseau (429, 5xx, timeout).</summary>
    Degraded,

    /// <summary>Clé refusée (401/403) ou fournisseur invalide.</summary>
    Blocked,

    /// <summary>Aucune connexion Internet disponible sur le poste.</summary>
    Offline
}

/// <summary>
/// Service de vérité d'état de l'assistant IA (L1 — Vérité).
/// Fait autorité pour le badge de la barre de titre, le bouton Générer,
/// l'état vide de l'aperçu et la page Paramètres.
/// </summary>
public interface IReadinessService
{
    /// <summary>État courant du service IA.</summary>
    ReadinessState State { get; }

    /// <summary>Titre descriptif lisible pour les enseignants (ex. « Assistant IA — prêt »).</summary>
    string StatusTitle { get; }

    /// <summary>Détails / infobulle expliquant l'état et le prochain geste utile.</summary>
    string StatusDetails { get; }

    /// <summary>Glyphe géométrique accessible pour WCAG 1.4.1 (○, ◐, ●, ▲, ■, 📡).</summary>
    string ShapeGlyph { get; }

    /// <summary>Fournisseur actif configuré (ex. « aistudio », « openai », « proxy »).</summary>
    string ActiveProviderKey { get; }

    /// <summary>Nom du modèle sélectionné.</summary>
    string ActiveModelName { get; }

    /// <summary>Dernier message d'erreur si l'état est Dégradé ou Bloqué.</summary>
    string? LastErrorMessage { get; }

    /// <summary>Indique si une génération peut être lancée immédiatement.</summary>
    bool CanGenerate { get; }

    /// <summary>Déclenché lors d'un changement d'état.</summary>
    event EventHandler? ReadinessChanged;

    /// <summary>Vérifie l'état actuel en interrogeant le coffre et les paramètres.</summary>
    Task RefreshAsync();

    /// <summary>Enregistre le résultat d'un appel réel pour mettre à jour l'état.</summary>
    void ReportExecutionOutcome(bool success, Exception? error = null);
}
