using Ecert.DocsReview.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ecert.DocsReview.Api.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<DocumentEvent> DocumentEvents => Set<DocumentEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.Property(d => d.Title).HasMaxLength(200);
            entity.Property(d => d.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(d => d.Status);

            entity.HasMany(d => d.Versions)
                .WithOne(v => v.Document)
                .HasForeignKey(v => v.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.Events)
                .WithOne(e => e.Document)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Ignore(d => d.CurrentVersion);
        });

        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.HasIndex(v => new { v.DocumentId, v.VersionNumber }).IsUnique();
            entity.Property(v => v.FileName).HasMaxLength(255);
            entity.Property(v => v.StoragePath).HasMaxLength(500);
            entity.Property(v => v.Sha256).HasMaxLength(64);
            entity.Property(v => v.UploadedBy).HasMaxLength(100);

            entity.HasMany(v => v.Observations)
                .WithOne(o => o.DocumentVersion)
                .HasForeignKey(o => o.DocumentVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Observation>(entity =>
        {
            entity.Property(o => o.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(o => o.Content).HasMaxLength(4000);
            entity.Property(o => o.CreatedBy).HasMaxLength(100);
        });

        modelBuilder.Entity<DocumentEvent>(entity =>
        {
            entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Details).HasMaxLength(1000);
            entity.Property(e => e.PerformedBy).HasMaxLength(100);
            entity.HasIndex(e => e.DocumentId);
        });
    }
}
