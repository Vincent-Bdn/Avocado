using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avocado.Server.Data;

/// <summary>
/// Used only by <c>dotnet ef migrations</c>. It points at a throwaway file rather than a real vault,
/// so generating a migration never needs a key and can never touch anyone's data.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AvocadoDbContext>
{
    public AvocadoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AvocadoDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "avocado-design-time.db")}")
            .Options;

        return new AvocadoDbContext(options);
    }
}
