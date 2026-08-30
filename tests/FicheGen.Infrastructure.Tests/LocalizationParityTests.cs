using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public sealed class LocalizationParityTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "FicheGen.Windows.slnx")) ||
                Directory.Exists(Path.Combine(dir, "src", "FicheGen.App", "Strings")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback to relative path from test runner
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void ResourcesFiles_AllThreeSupportedLanguages_MustExist()
    {
        var frPath = Path.Combine(SolutionRoot, "src", "FicheGen.App", "Strings", "fr-FR", "Resources.resw");
        var enPath = Path.Combine(SolutionRoot, "src", "FicheGen.App", "Strings", "en-US", "Resources.resw");
        var arPath = Path.Combine(SolutionRoot, "src", "FicheGen.App", "Strings", "ar-SA", "Resources.resw");

        File.Exists(frPath).Should().BeTrue($"fr-FR/Resources.resw should exist at {frPath}");
        File.Exists(enPath).Should().BeTrue($"en-US/Resources.resw should exist at {enPath}");
        File.Exists(arPath).Should().BeTrue($"ar-SA/Resources.resw should exist at {arPath}");
    }

    [Fact]
    public void ResourceKeys_MustHaveExactParityAcrossFrEnAr()
    {
        var frMap = LoadResourceMap("fr-FR");
        var enMap = LoadResourceMap("en-US");
        var arMap = LoadResourceMap("ar-SA");

        frMap.Count.Should().BeGreaterThan(700, "resource dictionary should be comprehensive");

        var missingInEn = frMap.Keys.Except(enMap.Keys).ToList();
        var missingInAr = frMap.Keys.Except(arMap.Keys).ToList();
        var extraInEn = enMap.Keys.Except(frMap.Keys).ToList();
        var extraInAr = arMap.Keys.Except(frMap.Keys).ToList();

        missingInEn.Should().BeEmpty("all French keys must be translated in English");
        missingInAr.Should().BeEmpty("all French keys must be translated in Arabic");
        extraInEn.Should().BeEmpty("no stray keys in English not present in French");
        extraInAr.Should().BeEmpty("no stray keys in Arabic not present in French");
    }

    [Fact]
    public void ResourceValues_MustNotContainReplacementCharacters()
    {
        var languages = new[] { "fr-FR", "en-US", "ar-SA" };

        foreach (var lang in languages)
        {
            var map = LoadResourceMap(lang);
            foreach (var (key, value) in map)
            {
                value.Should().NotContain("\uFFFD", $"key '{key}' in {lang} must not contain U+FFFD replacement character");
            }
        }
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public void ResourceFiles_MustNotContainDuplicateKeys(string cultureCode)
    {
        var path = Path.Combine(SolutionRoot, "src", "FicheGen.App", "Strings", cultureCode, "Resources.resw");
        var doc = XDocument.Load(path);

        var keys = doc.Root!.Elements("data")
            .Select(e => e.Attribute("name")?.Value)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        duplicates.Should().BeEmpty($"Resources.resw for {cultureCode} must not have duplicate keys");
    }

    [Fact]
    public void ResourceValues_MustNotBeEmptyOrWhitespace()
    {
        var languages = new[] { "fr-FR", "en-US", "ar-SA" };

        foreach (var lang in languages)
        {
            var map = LoadResourceMap(lang);
            foreach (var (key, value) in map)
            {
                value.Should().NotBeNullOrWhiteSpace($"key '{key}' in {lang} should have non-empty value");
            }
        }
    }

    [Fact]
    public void FormattedResourceStrings_MustHaveMatchingPlaceholders()
    {
        var frMap = LoadResourceMap("fr-FR");
        var enMap = LoadResourceMap("en-US");
        var arMap = LoadResourceMap("ar-SA");

        var placeholderRegex = new Regex(@"\{(\d+)(?::[^}]+)?\}", RegexOptions.Compiled);

        foreach (var (key, frVal) in frMap)
        {
            var frPlaceholders = placeholderRegex.Matches(frVal).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
            if (frPlaceholders.Count == 0) continue;

            if (enMap.TryGetValue(key, out var enVal))
            {
                var enPlaceholders = placeholderRegex.Matches(enVal).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
                enPlaceholders.Should().BeEquivalentTo(frPlaceholders,
                    $"key '{key}' in en-US should have identical format placeholders to fr-FR");
            }

            if (arMap.TryGetValue(key, out var arVal))
            {
                var arPlaceholders = placeholderRegex.Matches(arVal).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
                arPlaceholders.Should().BeEquivalentTo(frPlaceholders,
                    $"key '{key}' in ar-SA should have identical format placeholders to fr-FR");
            }
        }
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public void CoreUiKeys_MustBePresentInEveryLanguage(string cultureCode)
    {
        var map = LoadResourceMap(cultureCode);

        // Shell & navigation
        map.Should().ContainKey("Nav_FicheItem.Content");
        map.Should().ContainKey("Nav_EvaluationItem.Content");
        map.Should().ContainKey("Nav_QuizItem.Content");
        map.Should().ContainKey("Nav_HistoryItem.Content");
        map.Should().ContainKey("Nav_HelpItem.Content");

        // Fiche / Eval / Quiz headers
        map.Should().ContainKey("FP_Step1_Header.Text");
        map.Should().ContainKey("FP_Step2_Header.Text");
        map.Should().ContainKey("FP_Step3_Header.Text");
        map.Should().ContainKey("FP_Step4_Header.Text");
        map.Should().ContainKey("EP_Step1_Header.Text");
        map.Should().ContainKey("EP_Step2_Header.Text");
        map.Should().ContainKey("EP_Step3_Header.Text");
        map.Should().ContainKey("EP_Step4_Header.Text");
        map.Should().ContainKey("QP_Step1_Header.Text");
        map.Should().ContainKey("QP_Step2_Header.Text");
        map.Should().ContainKey("QP_Step3_Header.Text");
        map.Should().ContainKey("QP_Step4_Header.Text");

        // History
        map.Should().ContainKey("HP_Title.Text");
        map.Should().ContainKey("History_GroupToday");
        map.Should().ContainKey("History_GroupYesterday");
        map.Should().ContainKey("History_GroupThisWeek");
        map.Should().ContainKey("History_GroupOlder");
        map.Should().ContainKey("History_TypeFiche");
        map.Should().ContainKey("History_TypeEvaluation");
        map.Should().ContainKey("History_TypeQuiz");

        // Account Dialog
        map.Should().ContainKey("AD_Dialog.Title");
        map.Should().ContainKey("AD_SignInTitle.Text");
        map.Should().ContainKey("AD_SignUpTitle.Text");

        // Settings
        map.Should().ContainKey("SP_Title.Text");
        map.Should().ContainKey("SP_TabGeneral.Text");
        map.Should().ContainKey("SP_TabAi.Text");
        map.Should().ContainKey("SP_TabFolders.Text");
        map.Should().ContainKey("SP_TabStyles.Text");
        map.Should().ContainKey("SP_TabPrivacy.Text");
        map.Should().ContainKey("SP_TabPrompts.Text");
    }

    private static Dictionary<string, string> LoadResourceMap(string cultureCode)
    {
        var path = Path.Combine(SolutionRoot, "src", "FicheGen.App", "Strings", cultureCode, "Resources.resw");
        var doc = XDocument.Load(path);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dataElem in doc.Root!.Elements("data"))
        {
            var name = dataElem.Attribute("name")?.Value;
            var value = dataElem.Element("value")?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                map[name] = value ?? string.Empty;
            }
        }

        return map;
    }
}
