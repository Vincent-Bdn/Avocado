using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avocado.Server.Features.Deadlines.Enums;

namespace Avocado.Server.Features.Agenda.ValueObjects;

/// <summary>
/// An échéance as it should appear in the agenda: what Google is told, and nothing of how.
///
/// <para>Separate from <c>Deadline</c> because this is the part worth pinning. What a lawyer sees in
/// her calendar is a line of text in a list of thirty, usually on a telephone, and « Conclusions »
/// alone is useless when four dossiers have one that month.</para>
/// </summary>
/// <param name="RemindDaysBefore">
/// Her reminder, in days. Zero means the morning of, which Google expresses as a popup at the start
/// of an all-day event rather than as no reminder at all.
/// </param>
public sealed record AgendaEntry(
    Guid DeadlineId,
    string Label,
    DeadlineType Type,
    string MatterReference,
    string MatterName,
    DateOnly Date,
    TimeOnly? Time,
    int RemindDaysBefore,
    bool IsDone)
{
    /// <summary>How long a timed échéance is blocked out for, having no end time of its own.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    /// <summary>
    /// The one line she reads. « Audience de plaidoirie · COULEYRE / EDF ENR ».
    ///
    /// <para>The dossier is in the title rather than the description because a calendar shows the
    /// title and hides everything else, and the dossier is the half that tells her which of the four
    /// « Conclusions » this is.</para>
    /// </summary>
    public string Summary => $"{Label} · {MatterName}";

    /// <summary>
    /// What is under it when she opens the event: the reference, so she can find the dossier, and a
    /// line saying where this came from, so an event appearing in her agenda is never a mystery.
    /// </summary>
    public string Description =>
        $"Dossier {MatterReference} · {MatterName}\n"
        + $"Échéance {Labels.Of(Type)}{(IsDone ? ", faite" : string.Empty)}\n\n"
        + "Inscrite depuis Avocado. Les modifications faites ici sont écrasées à la prochaine synchronisation.";

    /// <summary>
    /// Everything the event is made of, hashed. Two passes over an unchanged échéance push nothing.
    ///
    /// <para>The version prefix is what makes a change to any of the text above take effect: without
    /// it, improving the description would leave every existing event on the old wording for ever,
    /// since nothing else about the échéance moved.</para>
    /// </summary>
    public string Fingerprint
    {
        get
        {
            var parts = string.Join(
                '\u001f',
                "v1",
                Summary,
                Description,
                Date.ToString("O", CultureInfo.InvariantCulture),
                Time?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                RemindDaysBefore.ToString(CultureInfo.InvariantCulture));

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(parts)))[..32];
        }
    }

    /// <summary>French for the handful of types, here rather than in the UI: this text leaves the machine.</summary>
    private static class Labels
    {
        public static string Of(DeadlineType type) => type switch
        {
            DeadlineType.Hearing => "audience",
            DeadlineType.ProceduralDeadline => "délai de procédure",
            DeadlineType.Appointment => "rendez-vous",
            DeadlineType.Other => "autre",
            _ => type.ToString().ToLowerInvariant(),
        };
    }
}
