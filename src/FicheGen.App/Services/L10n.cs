using System;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FicheGen.App.Services;

/// <summary>
/// Accès centralisé et dynamique aux ressources localisées (Strings/{lang}/Resources.resw).
/// </summary>
public static class L10n
{
    private static readonly ResourceManager Manager = new();
    private static readonly ResourceContext Context = Manager.CreateResourceContext();
    private static readonly ResourceMap Map = Manager.MainResourceMap.GetSubtree("Resources");

    public static string CurrentLanguage { get; private set; } = "fr-FR";

    public static void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) languageCode = "fr-FR";
        CurrentLanguage = languageCode;

        try
        {
            var culture = new CultureInfo(languageCode);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            Context.QualifierValues["Language"] = languageCode;
        }
        catch { }
    }

    /// <summary>Retourne la chaîne localisée pour la clé donnée, ou <paramref name="fallback"/>.</summary>
    public static string Get(string key, string fallback = "")
    {
        try
        {
            var candidate = Map?.GetValue(key, Context);
            if (candidate != null && !string.IsNullOrEmpty(candidate.ValueAsString))
            {
                return candidate.ValueAsString;
            }
        }
        catch { }

        return fallback;
    }

    /// <summary>Retourne la chaîne localisée mise en forme avec les arguments fournis.</summary>
    public static string Format(string key, params object[] args)
    {
        var raw = Get(key);
        return string.IsNullOrEmpty(raw) ? string.Empty : string.Format(raw, args);
    }
}
