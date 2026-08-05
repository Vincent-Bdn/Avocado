namespace Avocado.Server.Features.Documents.Enums;

/// <summary>The toolbar's segmented control: Tout · Pièces · Documents.</summary>
public enum DocumentSegment
{
    All,

    /// <summary>Pièces — documents carrying a numéro.</summary>
    Exhibits,

    /// <summary>Documents without a numéro.</summary>
    Documents,
}
