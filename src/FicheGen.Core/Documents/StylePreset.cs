namespace FicheGen.Core.Documents;

/// <summary>
/// Préréglage visuel d'un document généré. <see cref="HeaderLayout"/> choisit
/// le traitement artistique de l'en-tête (voir les constantes ci-dessous) ;
/// les autres propriétés pilotent couleurs, typographie, marges et arrondis.
/// </summary>
public sealed record StylePreset
{
    // Traitements d'en-tête pris en charge par le moteur de rendu.
    public const string HeaderRule = "Rule";         // Titre coloré + filet bicolore
    public const string HeaderBand = "Band";         // Bandeau dégradé, texte blanc
    public const string HeaderCentered = "Centered"; // Titre centré, filet double
    public const string HeaderMinimal = "Minimal";   // Titre sobre, pastille d'accent

    public required string Id { get; init; }
    public required string Name { get; init; }
    public string PrimaryColor { get; init; } = "#2563EB";
    public string SecondaryColor { get; init; } = "#1D4ED8";
    /// <summary>Couleur d'accentuation (encadrés, filet secondaire). Vide = Secondaire.</summary>
    public string AccentColor { get; init; } = "";
    public string FontFamily { get; init; } = "'Segoe UI', system-ui, sans-serif";
    public int MarginMm { get; init; } = 20;
    /// <summary>Marges par côté ; null = hérite de <see cref="MarginMm"/>.</summary>
    public int? MarginBottomMm { get; init; }
    public int? MarginLeftMm { get; init; }
    public int? MarginRightMm { get; init; }
    public double CornerRadiusPx { get; init; } = 10;
    public string HeaderLayout { get; init; } = HeaderRule;
    public string? CustomCss { get; init; }

    public static StylePreset Modern => new()
    {
        Id = "modern",
        Name = "Moderne",
        PrimaryColor = "#1E3A8A",
        SecondaryColor = "#2563EB",
        AccentColor = "#D97706",
        FontFamily = "'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif",
        MarginMm = 20,
        CornerRadiusPx = 8,
        HeaderLayout = HeaderRule
    };
}
