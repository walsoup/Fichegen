using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Microsoft.Windows.ApplicationModel.Resources;
using Serilog;

namespace FicheGen.App.Services;

/// <summary>
/// Accès centralisé et dynamique aux ressources localisées (Strings/{lang}/Resources.resw).
/// Supporte le changement de langue instantané au runtime avec bascule RTL/LTR et cache mémoire direct.
/// </summary>
public static class L10n
{
    private static readonly Dictionary<string, Dictionary<string, string>> ReswCache = new(StringComparer.OrdinalIgnoreCase);
    private static ResourceManager? _manager;
    private static ResourceContext? _context;
    private static ResourceMap? _map;
    private static ResourceLoader? _loader;

    public static string CurrentLanguage { get; private set; } = "fr-FR";
    public static event EventHandler<string>? LanguageChanged;

    static L10n()
    {
        LoadAllReswFiles();
        InitMrtCore();
    }

    private static void InitMrtCore()
    {
        try
        {
            var priPath = Path.Combine(AppContext.BaseDirectory, "resources.pri");
            _manager = File.Exists(priPath) ? new ResourceManager(priPath) : new ResourceManager();
            _context = _manager.CreateResourceContext();
            _context.QualifierValues["Language"] = CurrentLanguage;
            _map = _manager.MainResourceMap?.GetSubtree("Resources") ?? _manager.MainResourceMap;
            _loader = new ResourceLoader();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Initialisation de MRT Core ResourceManager impossible.");
        }
    }

    private static void LoadAllReswFiles()
    {
        var supported = new[] { "fr-FR", "en-US", "ar-SA" };
        var baseDir = AppContext.BaseDirectory;

        string? stringsDir = null;
        var cur = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && cur != null; i++)
        {
            var testPath = Path.Combine(cur.FullName, "Strings");
            if (Directory.Exists(testPath))
            {
                stringsDir = testPath;
                break;
            }
            var srcTestPath = Path.Combine(cur.FullName, "src", "FicheGen.App", "Strings");
            if (Directory.Exists(srcTestPath))
            {
                stringsDir = srcTestPath;
                break;
            }
            cur = cur.Parent;
        }

        if (stringsDir != null)
        {
            foreach (var lang in supported)
            {
                var filePath = Path.Combine(stringsDir, lang, "Resources.resw");
                if (File.Exists(filePath))
                {
                    try
                    {
                        var doc = XDocument.Load(filePath);
                        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var elem in doc.Descendants("data"))
                        {
                            var name = elem.Attribute("name")?.Value;
                            var val = elem.Element("value")?.Value;
                            if (!string.IsNullOrEmpty(name) && val != null)
                            {
                                dict[name] = val;
                            }
                        }
                        ReswCache[lang] = dict;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Chargement du fichier resw impossible pour la langue {Lang}", lang);
                    }
                }
            }
        }
    }

    public static void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) languageCode = "fr-FR";
        CurrentLanguage = languageCode;

        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode;
        }
        catch { }

        try
        {
            var culture = new CultureInfo(languageCode);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            if (_manager != null)
            {
                _context = _manager.CreateResourceContext();
                _context.QualifierValues["Language"] = languageCode;
                _map = _manager.MainResourceMap?.GetSubtree("Resources") ?? _manager.MainResourceMap;
            }
            _loader = new ResourceLoader();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Changement de culture impossible pour {Lang}", languageCode);
        }

        LanguageChanged?.Invoke(null, languageCode);
    }

    /// <summary>Retourne la chaîne localisée pour la clé donnée, ou <paramref name="fallback"/>.</summary>
    public static string Get(string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback;

        // 1. Dictionnaire en mémoire (100% garanti pour fr-FR, en-US, ar-SA)
        if (ReswCache.TryGetValue(CurrentLanguage, out var currentDict))
        {
            if (currentDict.TryGetValue(key, out var val)) return val;
            if (currentDict.TryGetValue($"{key}.Text", out var textVal)) return textVal;
            if (currentDict.TryGetValue($"{key}.Content", out var contentVal)) return contentVal;
            if (currentDict.TryGetValue($"{key}.Header", out var headerVal)) return headerVal;
        }

        // 2. MRT Core
        try
        {
            if (_map != null && _context != null)
            {
                var candidate = _map.GetValue(key, _context);
                if (candidate != null && !string.IsNullOrEmpty(candidate.ValueAsString))
                {
                    return candidate.ValueAsString;
                }
            }

            if (_manager?.MainResourceMap != null && _context != null)
            {
                var direct = _manager.MainResourceMap.GetValue(key, _context)
                          ?? _manager.MainResourceMap.GetValue($"Resources/{key}", _context);
                if (direct != null && !string.IsNullOrEmpty(direct.ValueAsString))
                {
                    return direct.ValueAsString;
                }
            }

            if (_loader != null)
            {
                var loaderStr = _loader.GetString(key);
                if (!string.IsNullOrEmpty(loaderStr))
                {
                    return loaderStr;
                }
            }
        }
        catch { }

        // 3. Repli sur le dictionnaire français
        if (CurrentLanguage != "fr-FR" && ReswCache.TryGetValue("fr-FR", out var frDict))
        {
            if (frDict.TryGetValue(key, out var val)) return val;
            if (frDict.TryGetValue($"{key}.Text", out var textVal)) return textVal;
            if (frDict.TryGetValue($"{key}.Content", out var contentVal)) return contentVal;
            if (frDict.TryGetValue($"{key}.Header", out var headerVal)) return headerVal;
        }

        return !string.IsNullOrEmpty(fallback) ? fallback : key;
    }

    /// <summary>Retourne la chaîne localisée mise en forme avec les arguments fournis.</summary>
    public static string Format(string key, params object[] args)
    {
        var raw = Get(key);
        return string.IsNullOrEmpty(raw) ? string.Empty : string.Format(raw, args);
    }
}
