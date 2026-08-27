using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TurboBoard.Persistence.Migrations;

[DbContext(typeof(TurboBoardDbContext))]
public sealed class TurboBoardDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.11");

        modelBuilder.Entity("TurboBoard.Persistence.DataSourceRecord", entity =>
        {
            entity.Property<Guid>("Id")
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset>("CreatedAtUtc")
                .HasColumnType("TEXT");

            entity.Property<Guid>("ConfigurationVersion")
                .HasColumnType("TEXT");

            entity.Property<string>("Description")
                .IsRequired()
                .HasMaxLength(2000)
                .HasColumnType("TEXT");

            entity.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnType("TEXT");

            entity.Property<string>("ProtectedSettings")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<string>("Provider")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset>("UpdatedAtUtc")
                .HasColumnType("TEXT");

            entity.HasKey("Id");

            entity.ToTable("DataSources", (string?)null);
        });

        modelBuilder.Entity("TurboBoard.Persistence.SchemaSnapshotRecord", entity =>
        {
            entity.Property<Guid>("DataSourceId")
                .HasColumnType("TEXT");

            entity.Property<Guid>("ConfigurationVersion")
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset>("DiscoveredAtUtc")
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset?>("LastRefreshAttemptedAtUtc")
                .HasColumnType("TEXT");

            entity.Property<string>("LastRefreshFailureMessage")
                .HasMaxLength(500)
                .HasColumnType("TEXT");

            entity.Property<string>("LastRefreshFailureStatus")
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            entity.Property<string>("SchemaJson")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.HasKey("DataSourceId");

            entity.ToTable("SchemaSnapshots", (string?)null);
        });

        modelBuilder.Entity("TurboBoard.Persistence.SavedQueryRecord", entity =>
        {
            entity.Property<Guid>("Id")
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset>("CreatedAtUtc")
                .HasColumnType("TEXT");

            entity.Property<Guid>("DataSourceId")
                .HasColumnType("TEXT");

            entity.Property<string>("DefinitionJson")
                .IsRequired()
                .HasColumnType("TEXT");

            entity.Property<string>("Description")
                .IsRequired()
                .HasMaxLength(2000)
                .HasColumnType("TEXT");

            entity.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset>("UpdatedAtUtc")
                .HasColumnType("TEXT");

            entity.HasKey("Id");

            entity.HasIndex("DataSourceId", "Name");

            entity.ToTable("SavedQueries", (string?)null);
        });

        modelBuilder.Entity("TurboBoard.Persistence.SavedQueryRecord", entity =>
        {
            entity.HasOne("TurboBoard.Persistence.DataSourceRecord", null)
                .WithMany()
                .HasForeignKey("DataSourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("TurboBoard.Persistence.SchemaSnapshotRecord", entity =>
        {
            entity.HasOne("TurboBoard.Persistence.DataSourceRecord", null)
                .WithOne()
                .HasForeignKey("TurboBoard.Persistence.SchemaSnapshotRecord", "DataSourceId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
#pragma warning restore 612, 618
    }
}
