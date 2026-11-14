using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlineCheckouts_Outcome",
                table: "OnlineCheckouts");

            migrationBuilder.AddColumn<int>(
                name: "QuoteDepositPercent",
                table: "TenantSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "Quotes",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuoteId",
                table: "OnlineCheckouts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_QuoteDepositPercent",
                table: "TenantSettings",
                sql: "[QuoteDepositPercent] IS NULL OR [QuoteDepositPercent] BETWEEN 10 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Quotes_Deposit",
                table: "Quotes",
                sql: "[DepositAmount] IS NULL OR ([DepositAmount] >= 10 AND [DepositAmount] <= [Total] AND [SentAt] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCheckouts_QuoteId",
                table: "OnlineCheckouts",
                column: "QuoteId",
                filter: "[QuoteId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlineCheckouts_Outcome",
                table: "OnlineCheckouts",
                sql: "([Status] = 0 AND ([Request] IS NOT NULL OR [QuoteId] IS NOT NULL) AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 1 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 2 AND [Request] IS NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NULL AND [Failure] IS NOT NULL AND [Failure] BETWEEN 1 AND 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlineCheckouts_Quote",
                table: "OnlineCheckouts",
                sql: "[QuoteId] IS NULL OR [Request] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_OnlineCheckouts_Quotes_QuoteId",
                table: "OnlineCheckouts",
                column: "QuoteId",
                principalTable: "Quotes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 31N (ADR 0094): rolling back would lose a deposit a customer paid for a quote (and the order it became), the
            // deposit a sent version asks, or the share a business chose. Once there is one, it stops before anything
            // changes; query 2 of verify.sql counts them. Without any, it drops only what Up added.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OnlineCheckouts] WITH (TABLOCKX, HOLDLOCK) WHERE [QuoteId] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [Quotes] WITH (TABLOCKX, HOLDLOCK) WHERE [DepositAmount] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [TenantSettings] WITH (TABLOCKX, HOLDLOCK) WHERE [QuoteDepositPercent] IS NOT NULL)
    THROW 50062, N''A business asks a deposit to accept its quotes, a quote asks one or a customer paid one; rolling back would lose it (query 2 of verify.sql).'', 1;');
");

            migrationBuilder.DropForeignKey(
                name: "FK_OnlineCheckouts_Quotes_QuoteId",
                table: "OnlineCheckouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_QuoteDepositPercent",
                table: "TenantSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Quotes_Deposit",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_OnlineCheckouts_QuoteId",
                table: "OnlineCheckouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlineCheckouts_Outcome",
                table: "OnlineCheckouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlineCheckouts_Quote",
                table: "OnlineCheckouts");

            migrationBuilder.DropColumn(
                name: "QuoteDepositPercent",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                table: "OnlineCheckouts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlineCheckouts_Outcome",
                table: "OnlineCheckouts",
                sql: "([Status] = 0 AND [Request] IS NOT NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 1 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 2 AND [Request] IS NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NULL AND [Failure] IS NOT NULL AND [Failure] BETWEEN 1 AND 4)");
        }
    }
}
