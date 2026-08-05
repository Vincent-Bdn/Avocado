using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Deadlines;
using Avocado.Server.Features.Documents;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.TimeEntries;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Data;

/// <summary>
/// One context for the whole vault. A DbContext cannot be sliced, and pretending otherwise only
/// produces ceremony — so it stays central while each entity's
/// <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{T}"/> lives beside the entity in
/// its own feature folder, collected here by assembly scan.
/// </summary>
public sealed class AvocadoDbContext(DbContextOptions<AvocadoDbContext> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Matter> Matters => Set<Matter>();
    public DbSet<MatterParty> MatterParties => Set<MatterParty>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Deadline> Deadlines => Set<Deadline>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<BillingInvoice> Invoices => Set<BillingInvoice>();
    public DbSet<BillingLedgerEntry> LedgerEntries => Set<BillingLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AvocadoDbContext).Assembly);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            // SQLite has no decimal type: EF stores decimals as TEXT, and then `ORDER BY amount`
            // sorts lexicographically. Money is long cents everywhere, and this makes reintroducing
            // a decimal a build-time failure rather than a subtly wrong total.
            if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
            {
                throw new InvalidOperationException(
                    $"{property.DeclaringType.ShortName()}.{property.Name} is a decimal. " +
                    "Store money as long cents.");
            }

            property.SetColumnName(ToSnakeCase(property.Name));
        }

        base.OnModelCreating(modelBuilder);
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                if (i > 0 && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                builder.Append(name[i]);
            }
        }

        return builder.ToString();
    }
}
