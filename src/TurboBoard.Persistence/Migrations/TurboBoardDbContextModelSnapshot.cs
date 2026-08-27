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
#pragma warning restore 612, 618
    }
}
