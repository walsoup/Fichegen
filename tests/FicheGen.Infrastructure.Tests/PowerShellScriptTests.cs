using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public sealed class PowerShellScriptTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "FicheGen.Windows.slnx")) ||
                Directory.Exists(Path.Combine(dir, "installer")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void InstallerScripts_MustHaveUtf8Bom()
    {
        var installerDir = Path.Combine(SolutionRoot, "installer");
        var psFiles = Directory.GetFiles(installerDir, "*.ps1", SearchOption.AllDirectories);

        psFiles.Should().NotBeEmpty("installer directory must contain PowerShell scripts");

        foreach (var file in psFiles)
        {
            var bytes = File.ReadAllBytes(file);
            bytes.Length.Should().BeGreaterThanOrEqualTo(3, $"script {Path.GetFileName(file)} should not be empty");
            var hasBom = bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            hasBom.Should().BeTrue($"script {Path.GetFileName(file)} must have UTF-8 BOM for Windows PowerShell 5.1 compatibility");
        }
    }

    [Fact]
    public void AllPowerShellScripts_MustParseWithoutErrors()
    {
        var scripts = Directory.GetFiles(Path.Combine(SolutionRoot, "installer"), "*.ps1", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(SolutionRoot, "scripts"), "*.ps1", SearchOption.AllDirectories))
            .ToList();

        scripts.Should().NotBeEmpty();

        foreach (var scriptPath in scripts)
        {
            // Verify AST parsing via powershell.exe
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"$errs = $null; $null = [System.Management.Automation.Language.Parser]::ParseFile('{scriptPath.Replace("'", "''")}', [ref]$null, [ref]$errs); if ($errs.Count -gt 0) {{ $errs | ForEach-Object {{ Write-Error $_.Message }}; exit 1 }} else {{ exit 0 }}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            proc.Should().NotBeNull();
            var stdErr = proc!.StandardError.ReadToEnd();
            proc.WaitForExit(10000).Should().BeTrue($"PowerShell parse check timed out for {Path.GetFileName(scriptPath)}");
            proc.ExitCode.Should().Be(0, $"Script {Path.GetFileName(scriptPath)} failed to parse: {stdErr}");
        }
    }
}
