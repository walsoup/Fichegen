using System.Threading;
using System.Threading.Tasks;

namespace FicheGen.Core.Abstractions;

/// <summary>
/// Service d'OCR hors-ligne pour la reconnaissance et l'extraction de texte à partir de documents numérisés (PDF scannés, photos, captures).
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Indique si la reconnaissance optique de caractères est supportée et disponible sur le système.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Extrait le texte d'une page spécifique d'un document PDF scanné.
    /// </summary>
    Task<string> ExtractTextFromPdfPageAsync(string pdfPath, int physicalPage, CancellationToken ct = default);

    /// <summary>
    /// Extrait le texte d'un fichier image (PNG, JPG, BMP, TIFF).
    /// </summary>
    Task<string> ExtractTextFromImageFileAsync(string imagePath, CancellationToken ct = default);

    /// <summary>
    /// Extrait le texte d'un tableau d'octets représentant une image.
    /// </summary>
    Task<string> ExtractTextFromImageBytesAsync(byte[] imageBytes, CancellationToken ct = default);
}
