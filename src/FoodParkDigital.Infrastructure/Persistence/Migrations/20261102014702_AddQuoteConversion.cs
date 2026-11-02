using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AppliedVisitCredit",
                table: "Quotes",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConvertedAt",
                table: "Quotes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "Quotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_OrderId",
                table: "Quotes",
                column: "OrderId",
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Quotes_Conversion",
                table: "Quotes",
                sql: "([OrderId] IS NULL AND [ConvertedAt] IS NULL AND [AppliedVisitCredit] IS NULL) OR ([OrderId] IS NOT NULL AND [ConvertedAt] IS NOT NULL AND [Status] = 2 AND ([AppliedVisitCredit] IS NULL OR ([AppliedVisitCredit] > 0 AND [VisitCreditAmount] IS NOT NULL AND [AppliedVisitCredit] <= [VisitCreditAmount] AND [AppliedVisitCredit] <= [Total])))");

            migrationBuilder.AddForeignKey(
                name: "FK_Quotes_Orders_OrderId",
                table: "Quotes",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A quote that became a service order would lose which order it became and what it applied of the visit (Fase 29E).
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [Quotes] WHERE [OrderId] IS NOT NULL OR [ConvertedAt] IS NOT NULL OR [AppliedVisitCredit] IS NOT NULL)
    THROW 50053, N''Accepted quotes already became service orders; rolling back would lose which order each one became and what it applied of the visit. Roll forward instead.'', 1;');
");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotes_Orders_OrderId",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_Quotes_OrderId",
                table: "Quotes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Quotes_Conversion",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "AppliedVisitCredit",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "ConvertedAt",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "Quotes");
        }
    }
}
