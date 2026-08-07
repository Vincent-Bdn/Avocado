namespace Avocado.Server.Features.Searches.Enums;

/// <summary>The palette's prefixes. Actions are resolved client-side, so they need no scope here.</summary>
public enum SearchScope
{
    All,

    /// <summary>`@`, tiers only.</summary>
    Contacts,

    /// <summary>`#`, documents and pièces only.</summary>
    Documents,

    Matters,
}
