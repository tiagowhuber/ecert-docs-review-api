using Ecert.DocsReview.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ecert.DocsReview.Tests;

public class DataSeederTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _storageRoot;

    public DataSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _storageRoot = Path.Combine(Path.GetTempPath(), $"ecert-seeder-tests-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task SeedAsync_PopulatesAnEmptyDatabase()
    {
        using var db = CreateContext();

        await DataSeeder.SeedAsync(db, _storageRoot);

        Assert.True(await db.Documents.AnyAsync());
        Assert.True(await db.DocumentVersions.AnyAsync());
        Assert.True(await db.Observations.AnyAsync());
        Assert.True(await db.DocumentEvents.AnyAsync());
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var db = CreateContext();

        await DataSeeder.SeedAsync(db, _storageRoot);
        var documentsAfterFirstRun = await db.Documents.CountAsync();

        await DataSeeder.SeedAsync(db, _storageRoot);

        Assert.Equal(documentsAfterFirstRun, await db.Documents.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_WritesEveryVersionFileToStorage()
    {
        using var db = CreateContext();

        await DataSeeder.SeedAsync(db, _storageRoot);

        var versions = await db.DocumentVersions.ToListAsync();
        Assert.NotEmpty(versions);
        foreach (var version in versions)
        {
            var fullPath = Path.Combine(_storageRoot, version.StoragePath);
            Assert.True(File.Exists(fullPath), $"Missing seeded file: {version.StoragePath}");

            var bytes = await File.ReadAllBytesAsync(fullPath);
            Assert.Equal(version.FileSizeBytes, bytes.Length);
            Assert.Equal(version.Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
            Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes[..5]));
        }
    }

    [Fact]
    public async Task SeedAsync_EveryDocumentHasAtLeastOneVersionAndACreationEvent()
    {
        using var db = CreateContext();

        await DataSeeder.SeedAsync(db, _storageRoot);

        var documents = await db.Documents
            .Include(d => d.Versions)
            .Include(d => d.Events)
            .ToListAsync();

        foreach (var document in documents)
        {
            Assert.NotEmpty(document.Versions);
            Assert.Contains(document.Events,
                e => e.EventType == Ecert.DocsReview.Api.Domain.DocumentEventType.DocumentCreated);
        }
    }
}
