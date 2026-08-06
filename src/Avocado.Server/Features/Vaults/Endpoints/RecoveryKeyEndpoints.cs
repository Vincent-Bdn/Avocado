using Avocado.Server.Features.Vaults.Enums;

namespace Avocado.Server.Features.Vaults.Endpoints;

/// <param name="Groups">
/// Index and value of the groups being checked, e.g. <c>{ 2: "6SCJ9Q", 7: "VNN5HT" }</c>. The design
/// asks for two of the nine, chosen at random: enough to prove the sheet was taken out and read,
/// short enough not to feel like an exam.
/// </param>
public sealed record RecoveryCheckRequest(Dictionary<int, string> Groups);

/// <param name="Correct">Per index, so a mistyped group is reported inline rather than as a verdict.</param>
public sealed record RecoveryCheckResponse(bool Passed, Dictionary<int, bool> Correct);

/// <param name="Code">
/// Null on a vault created before the recovery key was retained. Everything else in this response is
/// still meaningful; only the checks that need the key itself are unavailable.
/// </param>
public sealed record RecoveryKeyResponse(string? Code, string? Fingerprint, DateTimeOffset? CreatedAt);

/// <summary>
/// Réglages: verifying the printed sheet, and issuing a new key.
/// <para>
/// A recovery system nobody ever tested is one that does not work, which is why the check verifies
/// real groups against the real key rather than simply asking whether she thinks she still has it.
/// </para>
/// </summary>
public static class RecoveryKeyEndpoints
{
    public static IEndpointRouteBuilder MapRecoveryKey(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/vault/recovery-key").WithTags("Vault");

        group.MapGet("/", Get);
        group.MapPost("/check", Check);
        group.MapPost("/regenerate", Regenerate);

        return routes;
    }

    private static IResult Get(VaultSession session)
    {
        if (session.State != VaultState.Unlocked)
        {
            return Results.Problem(title: "Coffre fermé", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var vault = session.Get(Guid.Empty);
        var code = vault.RevealRecoveryCode();

        var created = vault.Keyring.Keys
            .Where(key => key.Kind == Vault.Keys.VaultKeyKind.Recovery)
            .Select(key => (DateTimeOffset?)key.CreatedAt)
            .FirstOrDefault();

        return Results.Ok(new RecoveryKeyResponse(code, Fingerprint(code), created));
    }

    private static IResult Check(RecoveryCheckRequest request, VaultSession session)
    {
        if (session.State != VaultState.Unlocked)
        {
            return Results.Problem(title: "Coffre fermé", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var code = session.Get(Guid.Empty).RevealRecoveryCode();
        if (code is null)
        {
            return Results.Problem(
                title: "Vérification impossible",
                detail: "Ce coffre a été créé avant que la clé ne soit conservée. "
                        + "Éditez une nouvelle clé pour pouvoir la contrôler.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var groups = code.Split('-');

        var correct = request.Groups.ToDictionary(
            entry => entry.Key,
            entry => entry.Key >= 0
                     && entry.Key < groups.Length
                     && string.Equals(
                         Normalise(entry.Value),
                         groups[entry.Key],
                         StringComparison.OrdinalIgnoreCase));

        return Results.Ok(new RecoveryCheckResponse(correct.Count > 0 && correct.Values.All(ok => ok), correct));
    }

    private static IResult Regenerate(VaultSession session)
    {
        if (session.State != VaultState.Unlocked)
        {
            return Results.Problem(title: "Coffre fermé", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Backups already taken keep opening with the previous key; only new ones use this. The UI
        // says so plainly, because deleting the old sheet too early would strand them.
        var code = session.Get(Guid.Empty).RegenerateRecoveryKey();

        return Results.Ok(new RecoveryKeyResponse(code, Fingerprint(code), DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Four bytes of SHA-256 over the code, as <c>4F2A·9C71</c>. Lets a sheet found in a drawer be
    /// matched to a vault without revealing anything about the key.
    /// </summary>
    private static string? Fingerprint(string? code)
    {
        if (code is null)
        {
            return null;
        }

        var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code));
        var hex = Convert.ToHexString(digest.AsSpan(0, 4));

        return $"{hex[..4]}·{hex[4..]}";
    }

    /// <summary>Same tolerance as the unlock field: case, spacing and the I/L/O confusions.</summary>
    private static string Normalise(string value) =>
        new([.. value.Trim().ToUpperInvariant()
            .Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c })
            .Where(char.IsLetterOrDigit)]);
}
