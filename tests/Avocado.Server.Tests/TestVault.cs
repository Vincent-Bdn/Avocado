using Avocado.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Tests;

/// <summary>
/// An Avocado database in memory, unencrypted, for the queries whose defects live in a LINQ filter
/// rather than in arithmetic a pure test can reach.
///
/// <para>Not a vault: no key, no blobs, no migrations. What is under test is what the query selects,
/// and SQLCipher would only make that slower to find out.</para>
/// </summary>
public sealed class TestVault : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");

    public TestVault()
    {
        _connection.Open();

        Database = new AvocadoDbContext(
            new DbContextOptionsBuilder<AvocadoDbContext>().UseSqlite(_connection).Options);

        Database.Database.EnsureCreated();
    }

    public AvocadoDbContext Database { get; }

    public T Save<T>(T entity) where T : class
    {
        Database.Add(entity);
        Database.SaveChanges();

        return entity;
    }

    public void Dispose()
    {
        Database.Dispose();
        _connection.Dispose();
    }
}
