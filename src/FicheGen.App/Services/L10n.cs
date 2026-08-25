using Microsoft.Windows.ApplicationModel.Resources;

namespace FicheGen.App.Services;

/// <summary>
/// Accès centralisé aux ressources localisées (Strings/fr-FR/Resources.resw).
/// Les chaînes de l'interface vivent dans les ressources ; cette aide évite
/// de répéter ResourceLoader dans chaque vue. Une clé manquante retourne la
/// valeur de repli fournie (jamais d'exception).
/// </summary>
public static class L10n
{
    private static readonly ResourceLoader Loader = new();

    /// <summary>Retourne la chaîne localisée pour la clé donnée, ou <paramref name="fallback"/>.</summary>
    public static string Get(string key, string fallback = "")
    {
        try
        {
            return Loader.GetString(key) is { Length: > 0 } value ? value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Retourne la chaîne localisée mise en forme avec les arguments fournis.</summary>
    public static string Format(string key, params object[] args)
        => string.Format(Get(key), args);
}
