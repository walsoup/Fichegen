using FicheGen.Core.Documents;

namespace FicheGen.Core.Abstractions;

public interface IDocxExporter
{
    byte[] ExportDocx(GeneratedDocument doc, StylePreset? preset = null);
    Task ExportDocxToFileAsync(GeneratedDocument doc, string filePath, StylePreset? preset = null, CancellationToken ct = default);
}
