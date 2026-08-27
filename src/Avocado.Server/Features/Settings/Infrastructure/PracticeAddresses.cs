using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Settings.Infrastructure;

/// <param name="Rejected">Kept so the message can name what was wrong rather than « adresse invalide ».</param>
public sealed record CleanedAddresses(IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected);

/// <summary>
/// The practice's own email addresses: whose message it is, which is the only thing that decides
/// whether the journal says reçu or envoyé.
///
/// <para><b>A whole domain counts as one entry.</b> The real export carries messages from eight
/// different people at @cvs-avocats.com, and listing them one by one would be tedious on the day and
/// wrong the day a colleague arrives. « @cvs-avocats.com » says what she means.</para>
/// </summary>
public static class PracticeAddresses
{
    /// <summary>Stored one per line; blank lines and stray separators are her typing, not an error.</summary>
    public static IReadOnlyList<string> Parse(string? stored) =>
        (stored ?? string.Empty)
            .Split(['\n', '\r', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(address => address.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// What she typed, tidied.
    ///
    /// <para>The test is one @ with something after it, and nothing more. Anything stricter refuses
    /// addresses that exist, and the cost of letting a wrong one through is that one message reads
    /// reçu when it was envoyé, which is a click to fix. Nothing before the @ makes it a domain.</para>
    /// </summary>
    public static CleanedAddresses Clean(IEnumerable<string>? typed)
    {
        var accepted = new List<string>();
        var rejected = new List<string>();

        foreach (var line in typed ?? [])
        {
            foreach (var address in Parse(line))
            {
                var at = address.IndexOf('@', StringComparison.Ordinal);

                if (at >= 0 && at < address.Length - 1 && address.IndexOf('@', at + 1) < 0)
                {
                    if (!accepted.Contains(address, StringComparer.Ordinal))
                    {
                        accepted.Add(address);
                    }
                }
                else
                {
                    rejected.Add(address);
                }
            }
        }

        return new CleanedAddresses(accepted, rejected);
    }

    /// <summary>
    /// Read once and handed to whatever is about to file messages. Not read per message: the real
    /// import reads 4 713 of them, and a query each would be 4 713 round trips for one answer that
    /// cannot change while it runs.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ReadAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var stored = await database.PracticeSettings
            .AsNoTracking()
            .Where(setting => setting.Key == PracticeSettingKeys.EmailAddresses)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return Parse(stored);
    }

    /// <summary>
    /// Whether this message was sent by the cabinet: the address itself, or anything at a domain she
    /// listed.
    /// </summary>
    public static bool IsOwn(string sender, IReadOnlyCollection<string> own)
    {
        if (own.Count == 0 || sender.Length == 0)
        {
            return false;
        }

        var at = sender.IndexOf('@', StringComparison.Ordinal);
        var domain = at >= 0 ? sender[at..] : null;

        foreach (var entry in own)
        {
            if (entry.StartsWith('@')
                ? domain is not null && domain.Equals(entry, StringComparison.OrdinalIgnoreCase)
                : entry.Equals(sender, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
