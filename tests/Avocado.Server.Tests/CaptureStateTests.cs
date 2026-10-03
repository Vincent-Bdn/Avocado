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
