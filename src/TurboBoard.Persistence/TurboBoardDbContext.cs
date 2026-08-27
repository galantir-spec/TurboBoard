using Microsoft.EntityFrameworkCore;

namespace TurboBoard.Persistence;

public sealed class TurboBoardDbContext(DbContextOptions<TurboBoardDbContext> options)
    : DbContext(options)
{
    public DbSet<DataSourceRecord> DataSources => Set<DataSourceRecord>();

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
    }
}
