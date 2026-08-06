using FicheGen.Core.Documents;

namespace FicheGen.Core.Abstractions;

public interface IRtfDocumentWriter
{
    string ExportRtfString(GeneratedDocument doc, StylePreset? preset = null);
    byte[] ExportRtfBytes(GeneratedDocument doc, StylePreset? preset = null);
}
