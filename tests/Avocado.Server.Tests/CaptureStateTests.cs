using Avocado.Server.Features.Backups.Infrastructure;
using Avocado.Server.Features.Settings;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Tests;

/// <summary>
/// The schedule and the last night's report live in the key/value settings table, which means they
/// go out as text and come back as text. A report that quietly failed to parse would leave the
/// screen saying « tout va bien » about a night that could not read four hundred files.
/// </summary>
public sealed class CaptureStateTests : IDisposable
{
    private readonly TestVault _vault = new();

    [Fact]
    public async Task DefaultsToTenInTheEveningBeforeSheHasChosenAnything()
    {
        var (schedule, capturedAt, report) = await CaptureState.ReadAsync(_vault.Database, default);

        Assert.True(schedule.IsEnabled);
        Assert.Equal(22, schedule.Hour);
        Assert.Null(capturedAt);
        Assert.Null(report);
    }

    [Fact]
    public async Task RemembersTheHourAndTheSwitch()
    {
        _vault.Save(new PracticeSetting { Key = PracticeSettingKeys.CaptureHour, Value = "3" });
        _vault.Save(new PracticeSetting { Key = PracticeSettingKeys.CaptureDocuments, Value = "false" });

        var (schedule, _, _) = await CaptureState.ReadAsync(_vault.Database, default);

        Assert.False(schedule.IsEnabled);
        Assert.Equal(3, schedule.Hour);
    }

    [Fact]
    public async Task CarriesLastNightsFailuresThroughToTheMorning()
    {
        var completedAt = new DateTimeOffset(2026, 3, 14, 22, 4, 0, TimeSpan.Zero);

        var report = new MirrorReport(
            Dossiers: 42,
            Copied: 31,
            CopiedBytes: 4_100_000,
            Files: 13_917,
            Bytes: 12_800_000_000,
            Unreadable: 3,
            Missing: [new DetachedFolder(Guid.NewGuid(), "2024-0007", "D:/Archives")],
            Issues:
            [
                new CaptureIssue("2026-0001", "Pièces/Pièce 4.pdf", "Accès refusé."),
                new CaptureIssue("2026-0009", "Conclusions.docx", "Le fichier est utilisé par un autre processus."),
            ]);

        await CaptureState.WriteAsync(_vault.Database, completedAt, report, default);
        await _vault.Database.SaveChangesAsync();

        var (_, capturedAt, read) = await CaptureState.ReadAsync(_vault.Database, default);

        Assert.Equal(completedAt, capturedAt);
        Assert.NotNull(read);
        Assert.Equal(13_917, read.Files);
        Assert.Equal(12_800_000_000, read.Bytes);
        Assert.Single(read.Missing);
        Assert.Equal("2024-0007", read.Missing[0].Dossier);
        Assert.Equal(3, read.Unreadable);
        Assert.Equal("Pièces/Pièce 4.pdf", read.Issues[0].Path);
        Assert.Equal("Accès refusé.", read.Issues[0].Reason);
    }

    /// <summary>
    /// A report written by the version before this one, read by this one.
    ///
    /// <para>This is not hypothetical: « Sauvegarde » threw a NullReferenceException on every
    /// practice whose last capture predated the field being added. A document missing a property
    /// does not fail to deserialize, it hands back null through a reference the record promises is a
    /// list, and the screen crashes counting it. The guard that was supposed to cover an older shape
    /// only caught malformed JSON, which this is not.</para>
    /// </summary>
    [Theory]
    // Before « missing » existed, which is the one that actually broke.
    [InlineData("""
                {"Dossiers":42,"Copied":31,"CopiedBytes":4100000,"Files":13917,"Bytes":12800000000,
                 "Unreadable":0,"Issues":[]}
                """)]
    // Before either list existed.
    [InlineData("""{"Dossiers":42,"Files":13917,"Bytes":12800000000}""")]
    // And a shape that names them explicitly as null, which is what a round trip through some other
    // serializer would write.
    [InlineData("""{"Dossiers":42,"Files":13917,"Missing":null,"Issues":null}""")]
    public async Task ReadsAReportWrittenBeforeTheseFieldsExisted(string json)
    {
        _vault.Save(new PracticeSetting { Key = PracticeSettingKeys.CaptureReport, Value = json });

        var (_, _, report) = await CaptureState.ReadAsync(_vault.Database, default);

        Assert.NotNull(report);
        Assert.Equal(13_917, report.Files);

        // Empty, never null: every screen that reads this counts them without checking.
        Assert.Empty(report.Missing);
        Assert.Empty(report.Issues);
        Assert.Equal(0, report.Missing.Count + report.Issues.Count);
    }

    [Fact]
    public async Task KeepsTheCountWhenThereAreFarTooManyToStore()
    {
        var issues = Enumerable.Range(1, 400)
            .Select(index => new CaptureIssue("2026-0001", $"Pièces/Pièce {index}.pdf", "Accès refusé."))
            .ToList();

        var report = MirrorReport.Empty with { Unreadable = issues.Count, Issues = issues };

        await CaptureState.WriteAsync(_vault.Database, DateTimeOffset.UtcNow, report, default);
        await _vault.Database.SaveChangesAsync();

        var stored = await _vault.Database.PracticeSettings
            .AsNoTracking()
            .SingleAsync(setting => setting.Key == PracticeSettingKeys.CaptureReport);

        // The column holds 2 000 characters. « 400 fichiers illisibles » is the sentence that matters
        // and it must survive even though the four hundred paths behind it cannot.
        Assert.True(stored.Value.Length < 2000, $"The report was {stored.Value.Length} characters.");

        var (_, _, read) = await CaptureState.ReadAsync(_vault.Database, default);

        Assert.NotNull(read);
        Assert.Equal(400, read.Unreadable);
        Assert.True(read.Issues.Count is > 0 and <= 10);
    }

    [Fact]
    public async Task SurvivesAReportWrittenByAnOlderVersion()
    {
        _vault.Save(new PracticeSetting
        {
            Key = PracticeSettingKeys.CaptureReport,
            Value = "{\"this\":[\"is not\",\"the shape it used to be\"",
        });

        var (schedule, _, report) = await CaptureState.ReadAsync(_vault.Database, default);

        // Unreadable, so absent. It must not take the Sauvegarde screen down with it.
        Assert.Null(report);
        Assert.True(schedule.IsEnabled);
    }

    public void Dispose() => _vault.Dispose();
}
