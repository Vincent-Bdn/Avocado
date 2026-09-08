using System.Text.Json;
using Avocado.Server.Data;
using Avocado.Server.Features.Settings;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Backups.Infrastructure;

/// <summary>
/// The schedule and the last run's outcome, in the key/value settings table.
///
/// <para>Four rows rather than four columns and a migration. What the capture needs to remember is
/// small, changes when she moves a slider, and has to be readable from the snapshot on a machine that
/// is restoring it, which the settings table already is.</para>
/// </summary>
public static class CaptureState
{
    private static readonly JsonSerializerOptions Format = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Only the first few, and the count is what carries the rest.
    ///
    /// <para>The setting's value is capped at two thousand characters, and « 400 fichiers illisibles »
    /// followed by four hundred paths is a screen nobody reads anyway. The ones shown are enough to
    /// recognise what kind of problem it is; <see cref="CaptureReport.Issues"/> keeps the count.</para>
    /// </summary>
    private const int IssuesKept = 6;

    public static async Task<(BackupSchedule Schedule, DateTimeOffset? CapturedAt, CaptureReport? Report)>
        ReadAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        var stored = await database.PracticeSettings
            .AsNoTracking()
            .Where(setting => setting.Key.StartsWith("backup.documents."))
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken)
            .ConfigureAwait(false);

        return (ScheduleFrom(stored), CapturedAtFrom(stored), ReportFrom(stored));
    }

    public static BackupSchedule ScheduleFrom(IReadOnlyDictionary<string, string> stored) =>
        new(
            !stored.TryGetValue(PracticeSettingKeys.CaptureDocuments, out var enabled)
                || !bool.TryParse(enabled, out var isEnabled)
                || isEnabled,
            stored.TryGetValue(PracticeSettingKeys.CaptureHour, out var hour) && int.TryParse(hour, out var parsed)
                ? parsed
                : BackupSchedule.Default.Hour);

    public static async Task WriteAsync(
        AvocadoDbContext database,
        DateTimeOffset capturedAt,
        CaptureReport report,
        CancellationToken cancellationToken)
    {
        await UpsertAsync(database, PracticeSettingKeys.CapturedAt, capturedAt.ToString("O"), cancellationToken)
            .ConfigureAwait(false);

        await UpsertAsync(
                database,
                PracticeSettingKeys.CaptureReport,
                JsonSerializer.Serialize(report with { Issues = report.Issues.Take(IssuesKept).ToList() }, Format),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static DateTimeOffset? CapturedAtFrom(IReadOnlyDictionary<string, string> stored) =>
        stored.TryGetValue(PracticeSettingKeys.CapturedAt, out var value)
        && DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static CaptureReport? ReportFrom(IReadOnlyDictionary<string, string> stored)
    {
        if (!stored.TryGetValue(PracticeSettingKeys.CaptureReport, out var json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CaptureReport>(json, Format);
        }
        catch (JsonException)
        {
            // Written by an older shape of this record. The next capture replaces it, and a report
            // that cannot be read is not worth failing the screen that shows it.
            return null;
        }
    }

    private static async Task UpsertAsync(
        AvocadoDbContext database,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var existing = await database.PracticeSettings
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            database.PracticeSettings.Add(new PracticeSetting { Key = key, Value = value });
            return;
        }

        existing.Value = value;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
