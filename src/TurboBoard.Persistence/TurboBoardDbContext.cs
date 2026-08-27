using Microsoft.EntityFrameworkCore;

namespace TurboBoard.Persistence;

public sealed class TurboBoardDbContext(DbContextOptions<TurboBoardDbContext> options)
    : DbContext(options)
{
    public DbSet<DataSourceRecord> DataSources => Set<DataSourceRecord>();

    public DbSet<SchemaSnapshotRecord> SchemaSnapshots => Set<SchemaSnapshotRecord>();

    public DbSet<SavedQueryRecord> SavedQueries => Set<SavedQueryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dataSource = modelBuilder.Entity<DataSourceRecord>();
        dataSource.ToTable("DataSources");
        dataSource.HasKey(item => item.Id);
        dataSource.Property(item => item.Id).ValueGeneratedNever();
        dataSource.Property(item => item.Name).HasMaxLength(200).IsRequired();
        dataSource.Property(item => item.Description).HasMaxLength(2000).IsRequired();
        dataSource.Property(item => item.Provider).HasMaxLength(64).IsRequired();
        dataSource.Property(item => item.ProtectedSettings).IsRequired();
        dataSource.Property(item => item.ConfigurationVersion).IsRequired();

        var schemaSnapshot = modelBuilder.Entity<SchemaSnapshotRecord>();
        schemaSnapshot.ToTable("SchemaSnapshots");
        schemaSnapshot.HasKey(item => item.DataSourceId);
        schemaSnapshot.Property(item => item.DataSourceId).ValueGeneratedNever();
        schemaSnapshot.Property(item => item.ConfigurationVersion).IsRequired();
        schemaSnapshot.Property(item => item.SchemaJson).IsRequired();
        schemaSnapshot.Property(item => item.LastRefreshFailureStatus).HasMaxLength(64);
        schemaSnapshot.Property(item => item.LastRefreshFailureMessage).HasMaxLength(500);
        schemaSnapshot.HasOne<DataSourceRecord>()
            .WithOne()
            .HasForeignKey<SchemaSnapshotRecord>(item => item.DataSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        var savedQuery = modelBuilder.Entity<SavedQueryRecord>();
        savedQuery.ToTable("SavedQueries");
        savedQuery.HasKey(item => item.Id);
        savedQuery.Property(item => item.Id).ValueGeneratedNever();
        savedQuery.Property(item => item.DataSourceId).IsRequired();
        savedQuery.Property(item => item.Name).HasMaxLength(200).IsRequired();
        savedQuery.Property(item => item.Description).HasMaxLength(2000).IsRequired();
        savedQuery.Property(item => item.DefinitionJson).IsRequired();
        savedQuery.HasOne<DataSourceRecord>()
            .WithMany()
            .HasForeignKey(item => item.DataSourceId)
            .OnDelete(DeleteBehavior.Cascade);
        savedQuery.HasIndex(item => new { item.DataSourceId, item.Name });
    }
}
