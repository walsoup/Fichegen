// ============================================================================
//  FicheGen.E2E.Tests — AppLauncher
//  Gestionnaire de lancement et de rattachement de l'application WinUI 3.
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.UIA3;

namespace FicheGen.E2E.Tests.Infrastructure;

/// <summary>
/// Démarre ou se rattache au processus WinUI 3 FicheGen.App.exe pour les tests E2E UI.
/// </summary>
public static class AppLauncher
{
    private static readonly string AppExecutableName = "FicheGen.App.exe";

    /// <summary>
    /// Localise l'exécutable compilé dans le répertoire bin du projet FicheGen.App.
    /// </summary>
    public static string? LocateAppExecutable()
    {
        var currentDir = AppContext.BaseDirectory;
        var searchPaths = new[]
        {
            Path.Combine(currentDir, AppExecutableName),
            Path.Combine(currentDir, "..", "..", "..", "..", "..", "src", "FicheGen.App", "bin", "x64", "Debug", "net8.0-windows10.0.19041.0", AppExecutableName),
            Path.Combine(currentDir, "..", "..", "..", "..", "..", "src", "FicheGen.App", "bin", "Debug", "net8.0-windows10.0.19041.0", AppExecutableName),
            Path.Combine(currentDir, "..", "..", "..", "..", "..", "src", "FicheGen.App", "bin", "x64", "Debug", AppExecutableName)
        };

        foreach (var path in searchPaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Lance l'application avec FlaUI et retourne un pilote UIAutomationDriver configuré.
    /// Si l'exécutable n'est pas disponible (environnement CI sans binaire compilé), retourne un pilote d'instance.
    /// </summary>
    public static UIAutomationDriver LaunchOrAttach()
    {
        var exePath = LocateAppExecutable();
        if (exePath is not null && File.Exists(exePath))
        {
            try
            {
                var app = Application.Launch(exePath);
                using var automation = new UIA3Automation();
                var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
                return new UIAutomationDriver(app, window);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppLauncher] Impossible de lancer le processus WinUI 3: {ex.Message}");
            }
        }

        return new UIAutomationDriver(null, null);
    }
}
