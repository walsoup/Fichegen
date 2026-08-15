using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace FicheGen.App.Services;

/// <summary>
/// Traduit les exceptions techniques et erreurs réseau/LLM en messages clairs et bienveillants pour les enseignants.
/// </summary>
public static class ErrorMessageTranslator
{
    public static string ToUserFriendlyMessage(Exception? ex)
    {
        if (ex is null) return "Une erreur inattendue est survenue.";

        if (ex is OperationCanceledException)
        {
            return "Opération annulée.";
        }

        if (ex is IOException ioEx)
        {
            var msg = ioEx.Message.ToLowerInvariant();
            if (msg.Contains("used by another process") || msg.Contains("sharing violation") || msg.Contains("processus") || msg.Contains("verrouillé"))
            {
                return "Impossible d'enregistrer : le fichier est actuellement ouvert dans Word ou votre lecteur PDF. Fermez-le puis réessayez.";
            }
            return "Impossible d'accéder au fichier demandé. Vérifiez qu'il n'est pas ouvert dans une autre application.";
        }

        if (ex is UnauthorizedAccessException)
        {
            return "Accès refusé au dossier ou fichier de destination. Choisissez un autre emplacement d'enregistrement.";
        }

        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                return code switch
                {
                    400 => "Requête refusée par le service IA. Vérifiez que votre clé d'accès est valide dans les Paramètres.",
                    401 or 403 => "Clé de connexion invalide ou non autorisée. Vérifiez votre clé dans les Paramètres.",
                    429 => "Limite d'utilisation atteinte auprès du service IA. Veuillez patienter une minute avant de réessayer.",
                    >= 500 => "Le service d'intelligence artificielle est momentanément indisponible. Réessayez dans quelques instants.",
                    _ => $"Connexion au service IA impossible (erreur {code}). Vérifiez votre connexion Internet."
                };
            }

            var msg = httpEx.Message.ToLowerInvariant();
            if (msg.Contains("host") || msg.Contains("name") || msg.Contains("connect") || msg.Contains("socket"))
            {
                return "Impossible de contacter le service IA. Vérifiez que votre ordinateur est bien connecté à Internet.";
            }

            return "Une erreur de communication avec le service IA est survenue. Vérifiez votre connexion Internet.";
        }

        if (ex is TimeoutException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Le service IA a mis trop de temps à répondre. Veuillez relancer la génération.";
        }

        if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            return "Le manuel ou fichier PDF sélectionné est introuvable ou inaccessible.";
        }

        if (ex is JsonException)
        {
            return "Le modèle d'IA a généré une réponse mal structurée. Réessayez en simplifiant votre sujet.";
        }

        var message = ex.Message;
        if (message.Contains("401", StringComparison.Ordinal) || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) || message.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase))
        {
            return "Clé de connexion invalide ou non reconnue. Rendez-vous dans les Paramètres pour vérifier votre configuration.";
        }
        if (message.Contains("429", StringComparison.Ordinal) || message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            return "Quota d'IA temporairement atteint. Veuillez patienter quelques instants avant de réessayer.";
        }

        return $"Une difficulté est survenue : {SanitizeMessage(message)}";
    }

    private static string SanitizeMessage(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var sanitized = System.Text.RegularExpressions.Regex.Replace(raw, @"(key|api[-_]?key|token)=([^\s&]+)", "$1=[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"(sk-[a-zA-Z0-9_-]{8,})", "sk-[REDACTED]");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"(AIza[a-zA-Z0-9_-]{10,})", "AIza[REDACTED]");
        return sanitized;
    }
}
