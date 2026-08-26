using System;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FicheGen.App.Services;

/// <summary>
/// Accès centralisé et dynamique aux ressources localisées (Strings/{lang}/Resources.resw).
/// Supporte le changement de langue instantané au runtime avec bascule RTL/LTR.
/// </summary>
public static class L10n
{
    private static readonly ResourceManager Manager = new();
    private static ResourceContext Context = Manager.CreateResourceContext();
    private static ResourceMap? Map = Manager.MainResourceMap?.GetSubtree("Resources");
    private static ResourceLoader? _loader;

    public static string CurrentLanguage { get; private set; } = "fr-FR";
    public static event EventHandler<string>? LanguageChanged;

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

            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode;

            Context = Manager.CreateResourceContext();
            Context.QualifierValues["Language"] = languageCode;
            Map = Manager.MainResourceMap?.GetSubtree("Resources");
            _loader = new ResourceLoader();
        }
        catch { }

        LanguageChanged?.Invoke(null, languageCode);
    }

    /// <summary>Retourne la chaîne localisée pour la clé donnée, ou <paramref name="fallback"/>.</summary>
    public static string Get(string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback;

        try
        {
            if (Map != null)
            {
                var candidate = Map.GetValue(key, Context);
                if (candidate != null && !string.IsNullOrEmpty(candidate.ValueAsString))
                {
                    return candidate.ValueAsString;
                }
            }

            if (Manager.MainResourceMap != null)
            {
                var direct = Manager.MainResourceMap.GetValue(key, Context)
                          ?? Manager.MainResourceMap.GetValue($"Resources/{key}", Context);
                if (direct != null && !string.IsNullOrEmpty(direct.ValueAsString))
                {
                    return direct.ValueAsString;
                }
            }

            _loader ??= new ResourceLoader();
            var loaderStr = _loader.GetString(key);
            if (!string.IsNullOrEmpty(loaderStr))
            {
                return loaderStr;
            }
        }
        catch { }

        return !string.IsNullOrEmpty(fallback) ? fallback : key;
    }

    /// <summary>Retourne la chaîne localisée mise en forme avec les arguments fournis.</summary>
    public static string Format(string key, params object[] args)
    {
        var raw = Get(key);
        return string.IsNullOrEmpty(raw) ? string.Empty : string.Format(raw, args);
    }
}
