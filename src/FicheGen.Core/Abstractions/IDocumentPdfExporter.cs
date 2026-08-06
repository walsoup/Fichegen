using FicheGen.Core.Documents;

namespace FicheGen.Core.Abstractions;

public interface IDocumentPdfExporter
{
    Task ExportPdfToFileAsync(GeneratedDocument doc, string outputPath, StylePreset? preset = null, CancellationToken ct = default);
}
