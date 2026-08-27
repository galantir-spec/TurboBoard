using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboBoard.Persistence.Migrations;

[DbContext(typeof(TurboBoardDbContext))]
[Migration("20260827030000_AddSavedQueries")]
public sealed class AddSavedQueries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SavedQueries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DataSourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavedQueries", x => x.Id);
                table.ForeignKey(
                    name: "FK_SavedQueries_DataSources_DataSourceId",
                    column: x => x.DataSourceId,
                    principalTable: "DataSources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SavedQueries_DataSourceId_Name",
            table: "SavedQueries",
            columns: new[] { "DataSourceId", "Name" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "SavedQueries");
}
