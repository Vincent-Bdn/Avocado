using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avocado.Server.Data;
using Avocado.Server.Features.Activities.Endpoints;
using Avocado.Server.Features.Billings.Endpoints;
using Avocado.Server.Features.Contacts.Endpoints;
using Avocado.Server.Features.Dashboards.Endpoints;
using Avocado.Server.Features.Documents.Endpoints;
using Avocado.Server.Features.Matters.Endpoints;
using Avocado.Server.Features.Searches.Endpoints;
using Avocado.Server.Features.TimeEntries.Endpoints;
using Avocado.Server.Features.Users.Endpoints;
using Avocado.Server.Hosting;
using Avocado.Vault;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var builder = WebApplication.CreateBuilder(args);

var vaultDirectory =
    builder.Configuration["vault"]
    ?? Environment.GetEnvironmentVariable("AVOCADO_VAULT")
    ?? throw new InvalidOperationException(
        "No vault folder configured. Pass --vault <folder> or set AVOCADO_VAULT.");

// Per-launch, injected by the Electron shell. Without it any web page in any browser on this machine
// could call the API: browsers happily send cross-origin requests to 127.0.0.1, and a DNS-rebinding
// page can read the responses. The random port is obscurity; this token is the actual control.
var apiToken =
    Environment.GetEnvironmentVariable("AVOCADO_API_TOKEN")
    ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

// Loopback only, and port 0 so the OS picks one. The shell learns both from the handshake below.
//
// Listen(IPAddress.Loopback, port) rather than ListenLocalhost(port): the latter binds both IPv4 and
// IPv6, and therefore rejects port 0 outright since it cannot guarantee the same free port on both.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(
    IPAddress.Loopback,
    int.TryParse(Environment.GetEnvironmentVariable("AVOCADO_PORT"), out var fixedPort) ? fixedPort : 0));

builder.Services.AddSingleton<IVaultStore>(_ =>
    new SingleVaultStore(VaultManager.UnlockWithDeviceKey(vaultDirectory)));
builder.Services.AddSingleton<VaultDbContextFactory>();
builder.Services.AddScoped(services =>
    new TenantContext(services.GetRequiredService<IVaultStore>().Get(Guid.Empty).Id));
builder.Services.AddScoped(services =>
    services.GetRequiredService<VaultDbContextFactory>()
        .Create(services.GetRequiredService<TenantContext>().VaultId));
builder.Services.AddScoped<CurrentUser>();

builder.Services.AddProblemDetails();

// Injected rather than DateTime.Now: every screen's urgency tiers and relative distances are computed
// against "today", and a fixed clock is the only way to test that boundary.
builder.Services.AddSingleton(TimeProvider.System);

// Enums cross the wire as their names, never as integers. The front end owns the French labels and
// maps from keys like `IncomingLetter`, so a renumbering here would silently relabel history.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<LocalApiTokenMiddleware>(apiToken);

var vault = app.Services.GetRequiredService<IVaultStore>().Get(Guid.Empty);
await VaultMigrator.EnsureUpToDateAsync(
    vault,
    app.Services.GetRequiredService<VaultDbContextFactory>(),
    app.Logger);

app.MapGet("/health", (IVaultStore store) =>
{
    var opened = store.Get(Guid.Empty);
    return Results.Ok(new
    {
        vaultId = opened.Id,
        folder = opened.Paths.Root,
        unlockPaths = opened.Keyring.Keys.Select(k => new { Kind = k.Kind.ToString(), k.Label }),
        hasRecoveryKey = opened.Keyring.HasRecoveryKey,
    });
});

app.MapUsers();
app.MapContacts();
app.MapMatters();
app.MapActivities();
app.MapDashboard();
app.MapSearch();
app.MapDocuments();
app.MapTimeEntries();
app.MapBilling();

// The shell reads this from stdout to learn where to point the window. Emitted once the host is
// actually listening, so the URL is real by the time anyone acts on it.
app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses ?? [];

    Console.WriteLine("AVOCADO_READY " + JsonSerializer.Serialize(new
    {
        url = addresses.FirstOrDefault(),
        token = apiToken,
        vaultId = vault.Id,
    }));
});

await app.RunAsync();

/// <summary>Exposed so the integration tests can drive the real host.</summary>
public partial class Program;

