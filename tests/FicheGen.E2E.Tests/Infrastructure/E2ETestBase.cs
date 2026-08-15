// ============================================================================
//  FicheGen.E2E.Tests — E2ETestBase
//  Classe de base xUnit pour les tests E2E avec cycle de vie IAsyncLifetime.
// ============================================================================

using System;
using System.Threading.Tasks;
using Xunit;

namespace FicheGen.E2E.Tests.Infrastructure;

/// <summary>
/// Classe de base d'exécution des tests E2E.
/// Initialise l'environnement de test et le pilote UI Automation.
/// </summary>
public abstract class E2ETestBase : IAsyncLifetime, IDisposable
{
    public TestEnvironment Environment { get; private set; } = null!;
    public UIAutomationDriver UIDriver { get; private set; } = null!;

    public virtual Task InitializeAsync()
    {
        Environment = TestEnvironmentFactory.Create();
        UIDriver = AppLauncher.LaunchOrAttach();
        return Task.CompletedTask;
    }

    public virtual Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        UIDriver?.Dispose();
        Environment?.Dispose();
        GC.SuppressFinalize(this);
    }
}
