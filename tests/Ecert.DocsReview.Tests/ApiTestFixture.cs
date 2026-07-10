using Ecert.DocsReview.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ecert.DocsReview.Tests;

/// <summary>
/// Boots the real API pipeline in-memory, swapping the Npgsql database for a
/// SQLite in-memory connection and pointing PDF storage at a throwaway temp
/// directory. One fixture per test keeps each test fully isolated.
/// </summary>
public class ApiTestFixture : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public string StorageRoot { get; }

    public ApiTestFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        StorageRoot = Path.Combine(Path.GetTempPath(), $"ecert-api-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(StorageRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Program requires these config values to exist; the connection string
        // is only a placeholder because the DbContext is swapped below.
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=localhost;Database=ecert-test;Username=postgres;Password=postgres");
        builder.UseSetting("Storage:RootPath", StorageRoot);

        builder.ConfigureTestServices(services =>
        {
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(AppDbContext))
                .ToList();
            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    /// <summary>Creates the schema on the shared in-memory connection.</summary>
    public void EnsureDatabaseCreated()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (Directory.Exists(StorageRoot))
            {
                Directory.Delete(StorageRoot, recursive: true);
            }
        }
    }
}
