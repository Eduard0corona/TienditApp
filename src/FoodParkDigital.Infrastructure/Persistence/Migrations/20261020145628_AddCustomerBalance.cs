using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerBalanceEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: true),
                    CashSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderRefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBalanceEntries", x => x.Id);
                    table.CheckConstraint("CK_CustomerBalanceEntries_Amount", "([Kind] IN (0, 1) AND [Amount] > 0) OR ([Kind] IN (2, 3) AND [Amount] < 0)");
                    table.CheckConstraint("CK_CustomerBalanceEntries_Ties", "([Kind] IN (0, 3) AND [Method] IS NOT NULL AND [CashSessionId] IS NOT NULL) OR ([Kind] = 1 AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL) OR ([Kind] = 2 AND [OrderId] IS NOT NULL AND [OrderPaymentId] IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalanceEntries_OrderPaymentId",
                table: "CustomerBalanceEntries",
                column: "OrderPaymentId",
                unique: true,
                filter: "[OrderPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalanceEntries_OrderRefundId",
                table: "CustomerBalanceEntries",
                column: "OrderRefundId",
                unique: true,
                filter: "[OrderRefundId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalanceEntries_TenantId_Phone_OccurredAt",
                table: "CustomerBalanceEntries",
                columns: new[] { "TenantId", "Phone", "OccurredAt" })
                .Annotation("SqlServer:Include", new[] { "Amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The balance in a customer's favor is money the business holds for them: rolling back would forget what they left,
            // and the previous version does not know the payments, refunds and drawer movements made with it, so once a
            // business uses it there is only roll-forward. The check runs as dynamic SQL, as the previous rollbacks do.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [CustomerBalanceEntries])
    OR EXISTS (SELECT 1 FROM [OrderPayments] WHERE [Method] = 7)
    OR EXISTS (SELECT 1 FROM [OrderRefunds] WHERE [Method] = 7)
    OR EXISTS (SELECT 1 FROM [CashTransactions] WHERE [TransactionType] IN (8, 9) OR [PaymentMethod] = 7)
    THROW 50029, N''Customers left a balance in their favor, or paid or were refunded with it, already; rolling back would forget what customers left and leave payments, refunds and drawer movements the previous version cannot read. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "CustomerBalanceEntries");
        }
    }
}
