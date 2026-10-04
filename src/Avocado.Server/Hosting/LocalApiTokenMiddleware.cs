using System.Security.Cryptography;
using System.Text;

namespace Avocado.Server.Hosting;

/// <summary>
/// Requires a per-launch bearer token on every request.
/// <para>
/// Binding to 127.0.0.1 is not a security boundary. Any process on the machine can reach the port,
/// and so can any web page the user has open: the browser will send the cross-origin request, and a
/// DNS-rebinding page can read the reply. The shell knows the token because it generated it and
/// passed it in the environment; nothing else does.
/// </para>
/// </summary>
public sealed class LocalApiTokenMiddleware(RequestDelegate next, string expectedToken)
{
    private readonly byte[] _expected = Encoding.UTF8.GetBytes(expectedToken);

    /// <summary>
    /// The one path a browser reaches without the token, because Google redirects her there and a
    /// browser has no way to carry it.
    ///
    /// <para>Safe to exempt because the handler refuses anything whose <c>state</c> was not minted by
    /// this process minutes earlier, and a page that cannot guess thirty-two bytes of randomness can
    /// do nothing here but read « Demande inconnue ». The reply is HTML sent with nosniff, so a
    /// cross-origin page cannot read it either.</para>
    /// </summary>
    private const string OAuthCallback = "/api/agenda/google/callback";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.Equals(OAuthCallback, StringComparison.Ordinal)
            && !IsAuthorised(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid Avocado API token.");
            return;
        }

        // The API is reached only by the shell over loopback; a browser must never be able to render
        // a response it tricked out of us.
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Cache-Control"] = "no-store";

        await next(context);
    }

    private bool IsAuthorised(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(header["Bearer ".Length..]);

        // Fixed-time, and length-safe: FixedTimeEquals returns false on a length mismatch rather
        // than leaking it through an early return.
        return CryptographicOperations.FixedTimeEquals(presented, _expected);
    }
}
