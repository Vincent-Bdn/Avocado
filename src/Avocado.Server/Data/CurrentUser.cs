using Avocado.Server.Features.Users;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Data;

/// <summary>
/// Who is working. The sibling of <see cref="TenantContext"/>: resolved once here, so no slice has to
/// know whether the practice has one lawyer or five.
/// <para>
/// Today it returns the single active user, creating one on first use so a fresh vault is never in a
/// state where nothing can be recorded. When there is real multi-user support this reads from the
/// authenticated principal instead, and nothing above it changes.
/// </para>
/// </summary>
public sealed class CurrentUser(AvocadoDbContext database)
{
    /// <summary>Fallback rate for a vault whose owner has not set one yet. 280 €/h.</summary>
    private const long DefaultHourlyRateCents = 28_000;

    private User? _resolved;

    public async Task<User> GetAsync(CancellationToken cancellationToken)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        _resolved = await database.Users
            .Where(user => user.IsActive)
            .OrderBy(user => user.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (_resolved is null)
        {
            // The setup wizard normally names the owner. This keeps a vault created by the CLI, or by
            // a restore, usable rather than failing on the first journal entry.
            _resolved = new User
            {
                DisplayName = "Utilisateur",
                HourlyRateCents = DefaultHourlyRateCents,
            };

            database.Users.Add(_resolved);
            await database.SaveChangesAsync(cancellationToken);
        }

        return _resolved;
    }
}
