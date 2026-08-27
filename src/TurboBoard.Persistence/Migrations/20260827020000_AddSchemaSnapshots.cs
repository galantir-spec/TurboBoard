using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboBoard.Persistence.Migrations;

[DbContext(typeof(TurboBoardDbContext))]
[Migration("20260827020000_AddSchemaSnapshots")]
public sealed class AddSchemaSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ConfigurationVersion",
            table: "DataSources",
            type: "TEXT",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.CreateTable(
            name: "SchemaSnapshots",
            columns: table => new
            {
                DataSourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                ConfigurationVersion = table.Column<Guid>(type: "TEXT", nullable: false),
                SchemaJson = table.Column<string>(type: "TEXT", nullable: false),
                DiscoveredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastRefreshFailureStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                LastRefreshFailureMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                LastRefreshAttemptedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SchemaSnapshots", x => x.DataSourceId);
                table.ForeignKey(
                    name: "FK_SchemaSnapshots_DataSources_DataSourceId",
                    column: x => x.DataSourceId,
                    principalTable: "DataSources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SchemaSnapshots");
        migrationBuilder.DropColumn(name: "ConfigurationVersion", table: "DataSources");
    }
}
