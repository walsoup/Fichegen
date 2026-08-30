using System;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public sealed class PackagingMetadataTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "FicheGen.Windows.slnx")) ||
                File.Exists(Path.Combine(dir, "Directory.Build.props")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void AppxManifest_And_BuildProps_MustHaveMatchingVersion()
    {
        var propsPath = Path.Combine(SolutionRoot, "Directory.Build.props");
        var manifestPath = Path.Combine(SolutionRoot, "src", "FicheGen.App", "Package.appxmanifest.xml");

        var propsDoc = XDocument.Load(propsPath);
        var versionElem = propsDoc.Descendants("Version").FirstOrDefault();
        versionElem.Should().NotBeNull();
        var propsVersion = versionElem!.Value.Trim();

        var manifestDoc = XDocument.Load(manifestPath);
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var identity = manifestDoc.Root?.Element(ns + "Identity");
        identity.Should().NotBeNull();
        var manifestVersion = identity!.Attribute("Version")?.Value;

        manifestVersion.Should().StartWith(propsVersion, "Package.appxmanifest.xml version must align with Directory.Build.props");
    }

    [Theory]
    [InlineData("x64", "1.4.0.0")]
    [InlineData("arm64", "1.4.0.0")]
    public void BuildInstallersScript_TransformsManifestCorrectlyForTargetArchitecture(string platform, string version)
    {
        var manifestPath = Path.Combine(SolutionRoot, "src", "FicheGen.App", "Package.appxmanifest.xml");
        var manifestContent = File.ReadAllText(manifestPath);

        var transformed = manifestContent
            .Replace("ProcessorArchitecture=\"x64\"", $"ProcessorArchitecture=\"{platform}\"")
            .Replace("Version=\"1.4.0.0\"", $"Version=\"{version}\"");

        var doc = XDocument.Parse(transformed);
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var identity = doc.Root?.Element(ns + "Identity");

        identity.Should().NotBeNull();
        identity!.Attribute("ProcessorArchitecture")?.Value.Should().Be(platform);
        identity.Attribute("Version")?.Value.Should().Be(version);
    }
}
