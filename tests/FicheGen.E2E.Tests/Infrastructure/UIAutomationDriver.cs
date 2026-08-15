// ============================================================================
//  FicheGen.E2E.Tests — UIAutomationDriver
//  Wrapper autour de FlaUI (UIA3) et des abstractions UI Automation.
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FicheGen.E2E.Tests.Infrastructure;

/// <summary>
/// Service d'automation UI encapsulant FlaUI et UI Automation
/// pour interagir de manière robuste avec les fenêtres et éléments WinUI 3.
/// </summary>
public sealed class UIAutomationDriver : IDisposable
{
    private readonly Application? _app;
    private readonly Window? _mainWindow;
    private bool _disposed;

    public Application? App => _app;
    public Window? MainWindow => _mainWindow;
    public bool IsAttached => _app is not null && !_app.HasExited;

    public UIAutomationDriver(Application? app = null, Window? mainWindow = null)
    {
        _app = app;
        _mainWindow = mainWindow;
    }

    /// <summary>
    /// Recherche un élément UI par son AutomationId avec un délai d'attente configurable.
    /// </summary>
    public AutomationElement? FindElementByAutomationId(string automationId, TimeSpan? timeout = null)
    {
        if (_mainWindow is null) return null;
        var maxWait = timeout ?? TimeSpan.FromSeconds(5);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < maxWait)
        {
            var element = _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element is not null) return element;
            Task.Delay(100).Wait();
        }

        return null;
    }

    /// <summary>
    /// Recherche un élément UI par son nom (Name).
    /// </summary>
    public AutomationElement? FindElementByName(string name, TimeSpan? timeout = null)
    {
        if (_mainWindow is null) return null;
        var maxWait = timeout ?? TimeSpan.FromSeconds(5);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < maxWait)
        {
            var element = _mainWindow.FindFirstDescendant(cf => cf.ByName(name));
            if (element is not null) return element;
            Task.Delay(100).Wait();
        }

        return null;
    }

    /// <summary>
    /// Effectue un clic sur l'élément spécifié par son AutomationId.
    /// </summary>
    public bool ClickButton(string automationId, TimeSpan? timeout = null)
    {
        var element = FindElementByAutomationId(automationId, timeout);
        if (element is null) return false;

        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
        }
        else
        {
            element.Click();
        }
        return true;
    }

    /// <summary>
    /// Saisit du texte dans un champ d'entrée trouvé par AutomationId.
    /// </summary>
    public bool EnterText(string automationId, string text, TimeSpan? timeout = null)
    {
        var element = FindElementByAutomationId(automationId, timeout);
        if (element is null) return false;

        if (element.Patterns.Value.IsSupported)
        {
            element.Patterns.Value.Pattern.SetValue(text);
        }
        else
        {
            element.Focus();
            element.Click();
            FlaUI.Core.Input.Keyboard.Type(text);
        }
        return true;
    }

    /// <summary>
    /// Récupère la valeur textuelle d'un élément UI.
    /// </summary>
    public string? GetElementText(string automationId, TimeSpan? timeout = null)
    {
        var element = FindElementByAutomationId(automationId, timeout);
        if (element is null) return null;

        if (element.Patterns.Value.IsSupported)
        {
            return element.Patterns.Value.Pattern.Value.Value;
        }

        return element.Name;
    }

    /// <summary>
    /// Vérifie si un élément UI est présent et visible.
    /// </summary>
    public bool IsElementVisible(string automationId, TimeSpan? timeout = null)
    {
        var element = FindElementByAutomationId(automationId, timeout);
        return element is not null && !element.IsOffscreen;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_app is not null && !_app.HasExited)
        {
            try
            {
                _app.Close();
            }
            catch
            {
                // Ignorer en fermeture de test
            }
        }
    }
}
