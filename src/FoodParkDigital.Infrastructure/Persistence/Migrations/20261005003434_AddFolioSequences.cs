using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFolioSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FolioSequences",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LastNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolioSequences", x => new { x.TenantId, x.Scope });
                    table.ForeignKey(
                        name: "FK_FolioSequences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Each business continues after its highest order folio ("ORD-" + number). Daily counters (inventory
            // movements, purchase orders) are created on their first use of the day, after that day's folios.
            migrationBuilder.Sql("""
                INSERT INTO [FolioSequences] ([TenantId], [Scope], [LastNumber])
                SELECT t.[Id], N'ORD', ISNULL(MAX(TRY_CAST(SUBSTRING(o.[OrderNumber], 5, 18) AS bigint)), 0)
                FROM [Tenants] t
                LEFT JOIN [Orders] o ON o.[TenantId] = t.[Id] AND o.[OrderNumber] LIKE N'ORD-%'
                GROUP BY t.[Id];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolioSequences");
        }
    }
}
