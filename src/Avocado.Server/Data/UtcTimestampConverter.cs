using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Avocado.Server.Data;

/// <summary>
/// Stores every <see cref="DateTimeOffset"/> as a fixed-width ISO-8601 string normalised to UTC.
/// <para>
/// EF Core refuses to translate <c>ORDER BY</c> over a <c>DateTimeOffset</c> on SQLite, because its
/// default storage keeps the original offset and text ordering would then disagree with chronological
/// ordering. Without this converter every "most recent" query in the application — the journal, the
/// accueil's recent dossiers, ⌘K, the *Dernière activité* column — throws at runtime rather than at
/// build time.
/// </para>
/// <para>
/// Normalising to UTC first makes the suffix identical on every row, so lexicographic order is
/// chronological order. Text rather than ticks on purpose: the vault's promise is that the data
/// outlives the application, and <c>2026-03-13T17:04:00.0000000+00:00</c> is legible to whoever opens
/// the file in ten years, where <c>638774...</c> is not.
/// </para>
/// </summary>
public sealed class UtcTimestampConverter() : ValueConverter<DateTimeOffset, string>(
    value => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
    value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
