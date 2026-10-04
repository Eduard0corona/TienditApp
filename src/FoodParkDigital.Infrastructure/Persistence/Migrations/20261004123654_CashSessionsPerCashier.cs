using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashSessionsPerCashier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashSessions_TenantId_SingleOpen",
                table: "CashSessions");

            migrationBuilder.AddColumn<bool>(
                name: "RequireOpenCashSession",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CashCountJson",
                table: "CashSessions",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingDifference",
                table: "CashSessions",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosingNotes",
                table: "CashSessions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedClosingAmount",
                table: "CashSessions",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryLocationId",
                table: "CashSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OpenedByUserId",
                table: "CashSessions",
                type: "uniqueidentifier",
                nullable: true);

            // Existing drawers: the branch is the business's default location (cash had no branch before),
            // the owner is the member whose email opened it, and closed drawers get the expected amount and
            // the difference they had, computed from their cash movements. EXEC defers compilation until
            // the columns above exist (the idempotent script compiles each batch before running it).
            migrationBuilder.Sql("""
                EXEC(N'UPDATE [cs]
                SET [InventoryLocationId] = (
                    SELECT TOP (1) [il].[Id]
                    FROM [InventoryLocations] AS [il]
                    WHERE [il].[TenantId] = [cs].[TenantId] AND [il].[IsActive] = 1
                    ORDER BY [il].[IsDefault] DESC, [il].[Name])
                FROM [CashSessions] AS [cs]
                WHERE [cs].[InventoryLocationId] IS NULL');
                """);

            migrationBuilder.Sql("""
                EXEC(N'UPDATE [cs]
                SET [OpenedByUserId] = [u].[Id]
                FROM [CashSessions] AS [cs]
                INNER JOIN [AspNetUsers] AS [u] ON [u].[NormalizedEmail] = UPPER([cs].[OpenedBy])
                INNER JOIN [TenantUsers] AS [tu] ON [tu].[UserId] = [u].[Id] AND [tu].[TenantId] = [cs].[TenantId]
                WHERE [cs].[OpenedByUserId] IS NULL');
                """);

            migrationBuilder.Sql("""
                EXEC(N'UPDATE [cs]
                SET [ExpectedClosingAmount] = [cs].[OpeningAmount] + ISNULL((
                    SELECT SUM([t].[Amount])
                    FROM [CashTransactions] AS [t]
                    WHERE [t].[CashSessionId] = [cs].[Id]
                      AND ([t].[PaymentMethod] IS NULL OR [t].[PaymentMethod] = 0)), 0)
                FROM [CashSessions] AS [cs]
                WHERE [cs].[Status] = 1 AND [cs].[ExpectedClosingAmount] IS NULL');
                """);

            migrationBuilder.Sql("""
                EXEC(N'UPDATE [CashSessions]
                SET [ClosingDifference] = [ClosingAmount] - [ExpectedClosingAmount]
                WHERE [Status] = 1
                  AND [ClosingAmount] IS NOT NULL
                  AND [ExpectedClosingAmount] IS NOT NULL
                  AND [ClosingDifference] IS NULL');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_InventoryLocationId",
                table: "CashSessions",
                column: "InventoryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_InventoryLocationId_OpenedAt",
                table: "CashSessions",
                columns: new[] { "TenantId", "InventoryLocationId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_OpenedByUserId_Open",
                table: "CashSessions",
                columns: new[] { "TenantId", "OpenedByUserId" },
                unique: true,
                filter: "[Status] = 0 AND [OpenedByUserId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CashSessions_InventoryLocations_InventoryLocationId",
                table: "CashSessions",
                column: "InventoryLocationId",
                principalTable: "InventoryLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashSessions_InventoryLocations_InventoryLocationId",
                table: "CashSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashSessions_InventoryLocationId",
                table: "CashSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashSessions_TenantId_InventoryLocationId_OpenedAt",
                table: "CashSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashSessions_TenantId_OpenedByUserId_Open",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "RequireOpenCashSession",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CashCountJson",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "ClosingDifference",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "ClosingNotes",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "ExpectedClosingAmount",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "InventoryLocationId",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "OpenedByUserId",
                table: "CashSessions");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_SingleOpen",
                table: "CashSessions",
                column: "TenantId",
                unique: true,
                filter: "[Status] = 0");
        }
    }
}
