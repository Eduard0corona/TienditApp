using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlinePaymentReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Link",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Settled",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Status",
                table: "OnlinePaymentRefunds");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaymentsReviewedUntil",
                table: "OnlinePaymentAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Dispute",
                table: "OnlineCheckouts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DisputeUpdatedAt",
                table: "OnlineCheckouts",
                type: "datetimeoffset",
                nullable: true);

            // Each refund of Mercado Pago is recorded once from now on: two rows with the same id would stop the unique index,
            // so they stop the migration first with a message that says so (Mercado Pago gives each refund its own id).
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OnlinePaymentRefunds] WHERE [ProviderRefundId] IS NOT NULL GROUP BY [TenantId], [ProviderRefundId] HAVING COUNT(*) > 1)
    THROW 50036, N''Two online refunds of a business have the same Mercado Pago refund id; check them before applying the migration.'', 1;');
");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentRefunds_TenantId_PaymentId_Chargeback",
                table: "OnlinePaymentRefunds",
                columns: new[] { "TenantId", "PaymentId" },
                unique: true,
                filter: "[Reason] = 6");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentRefunds_TenantId_ProviderRefundId",
                table: "OnlinePaymentRefunds",
                columns: new[] { "TenantId", "ProviderRefundId" },
                unique: true,
                filter: "[ProviderRefundId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Link",
                table: "OnlinePaymentRefunds",
                sql: "([Reason] IN (1, 2) AND [CheckoutId] IS NOT NULL AND [OrderId] IS NULL AND [OrderRefundId] IS NULL) OR ([Reason] IN (3, 4, 5, 6) AND [CheckoutId] IS NULL AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlinePaymentRefunds_MadeInMercadoPago",
                table: "OnlinePaymentRefunds",
                sql: "[Reason] NOT IN (4, 5, 6) OR ([Status] = 1 AND ([Reason] = 6 OR [ProviderRefundId] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Settled",
                table: "OnlinePaymentRefunds",
                sql: "([Status] = 0 AND [SettledAt] IS NULL AND [FailureMessage] IS NULL) OR ([Status] = 1 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NULL) OR ([Status] = 2 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NOT NULL) OR ([Status] = 3 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NOT NULL AND [ProviderRefundId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Status",
                table: "OnlinePaymentRefunds",
                sql: "[Status] IN (0, 1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCheckouts_TenantId_Dispute",
                table: "OnlineCheckouts",
                columns: new[] { "TenantId", "Dispute" },
                filter: "[Dispute] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlineCheckouts_Dispute",
                table: "OnlineCheckouts",
                sql: "([Dispute] = 0 AND [DisputeUpdatedAt] IS NULL) OR ([Dispute] IN (1, 2, 3) AND [Status] = 1 AND [DisputeUpdatedAt] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // What went back in Mercado Pago is already on the orders as their refunds, and a dispute is what Mercado Pago
            // said: the previous version cannot read them, and dropping them would lose why an order was refunded. Once any
            // exists there is only roll-forward. The check runs as dynamic SQL: a second run of rollback.sql, with the
            // columns gone, skips it without compiling it.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OnlinePaymentRefunds] WHERE [Status] = 3 OR [Reason] IN (4, 5, 6))
    OR EXISTS (SELECT 1 FROM [OnlineCheckouts] WHERE [Dispute] <> 0)
    THROW 50035, N''Refunds and disputes from Mercado Pago are on the orders already; rolling back would leave refunds the previous version cannot read. Roll forward instead.'', 1;');
");

            migrationBuilder.DropIndex(
                name: "IX_OnlinePaymentRefunds_TenantId_PaymentId_Chargeback",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropIndex(
                name: "IX_OnlinePaymentRefunds_TenantId_ProviderRefundId",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Link",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlinePaymentRefunds_MadeInMercadoPago",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Settled",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Status",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropIndex(
                name: "IX_OnlineCheckouts_TenantId_Dispute",
                table: "OnlineCheckouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlineCheckouts_Dispute",
                table: "OnlineCheckouts");

            migrationBuilder.DropColumn(
                name: "PaymentsReviewedUntil",
                table: "OnlinePaymentAccounts");

            migrationBuilder.DropColumn(
                name: "Dispute",
                table: "OnlineCheckouts");

            migrationBuilder.DropColumn(
                name: "DisputeUpdatedAt",
                table: "OnlineCheckouts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Link",
                table: "OnlinePaymentRefunds",
                sql: "([Reason] IN (1, 2) AND [CheckoutId] IS NOT NULL AND [OrderId] IS NULL AND [OrderRefundId] IS NULL) OR ([Reason] = 3 AND [CheckoutId] IS NULL AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Settled",
                table: "OnlinePaymentRefunds",
                sql: "([Status] = 0 AND [SettledAt] IS NULL AND [FailureMessage] IS NULL) OR ([Status] = 1 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NULL) OR ([Status] = 2 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Status",
                table: "OnlinePaymentRefunds",
                sql: "[Status] IN (0, 1, 2)");
        }
    }
}
