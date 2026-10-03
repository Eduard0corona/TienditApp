using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashSessionSingleOpenAndRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "CashSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Data repair required by the unique index: if a tenant has more than one open session
            // (possible before this change through concurrent "open" requests), keep the most recent one
            // open and close the older ones without a counted amount, leaving a note for review.
            // This step cannot be undone by Down().
            migrationBuilder.Sql("""
                WITH Ranked AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY TenantId ORDER BY OpenedAt DESC, Id DESC) AS RowNumber
                    FROM CashSessions
                    WHERE Status = 0
                )
                UPDATE cs
                SET Status = 1,
                    ClosedAt = SYSDATETIMEOFFSET(),
                    ClosedBy = N'system-migration',
                    Notes = LEFT(CONCAT(COALESCE(cs.Notes + N' | ', N''),
                        N'Cerrada automaticamente al migrar: el negocio tenia otra caja abierta.'), 2000)
                FROM CashSessions AS cs
                INNER JOIN Ranked AS r ON r.Id = cs.Id
                WHERE r.RowNumber > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_SingleOpen",
                table: "CashSessions",
                column: "TenantId",
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashSessions_TenantId_SingleOpen",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "CashSessions");
        }
    }
}
