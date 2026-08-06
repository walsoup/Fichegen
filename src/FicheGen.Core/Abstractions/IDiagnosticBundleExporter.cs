namespace FicheGen.Core.Abstractions;

public interface IDiagnosticBundleExporter
{
    Task<string> ExportDiagnosticBundleAsync(string destinationZipPath, CancellationToken ct = default);
}
