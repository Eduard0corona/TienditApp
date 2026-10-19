using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlinePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OnlineAmount",
                table: "DailySalesSnapshots",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "OnlineCheckouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderUserId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Request = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CustomerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreferenceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PaymentId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Failure = table.Column<int>(type: "int", nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineCheckouts", x => x.Id);
                    table.CheckConstraint("CK_OnlineCheckouts_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_OnlineCheckouts_Outcome", "([Status] = 0 AND [Request] IS NOT NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 1 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 2 AND [Request] IS NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NULL AND [Failure] IS NOT NULL AND [Failure] BETWEEN 1 AND 4)");
                    table.CheckConstraint("CK_OnlineCheckouts_Status", "[Status] IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_OnlineCheckouts_AspNetUsers_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OnlineCheckouts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OnlineCheckouts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OnlinePaymentAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderUserId = table.Column<long>(type: "bigint", nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LiveMode = table.Column<bool>(type: "bit", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConnectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptsPayments = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlinePaymentAccounts", x => x.Id);
                    table.CheckConstraint("CK_OnlinePaymentAccounts_ProviderUserId", "[ProviderUserId] > 0");
                    table.ForeignKey(
                        name: "FK_OnlinePaymentAccounts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OnlinePaymentAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeVerifier = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlinePaymentAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlinePaymentAuthorizations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OnlinePaymentAuthorizations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OnlinePaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    CheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderRefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderRefundId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlinePaymentRefunds", x => x.Id);
                    table.CheckConstraint("CK_OnlinePaymentRefunds_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_OnlinePaymentRefunds_Link", "([Reason] IN (1, 2) AND [CheckoutId] IS NOT NULL AND [OrderId] IS NULL AND [OrderRefundId] IS NULL) OR ([Reason] = 3 AND [CheckoutId] IS NULL AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL)");
                    table.CheckConstraint("CK_OnlinePaymentRefunds_Settled", "([Status] = 0 AND [SettledAt] IS NULL AND [FailureMessage] IS NULL) OR ([Status] = 1 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NULL) OR ([Status] = 2 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NOT NULL)");
                    table.CheckConstraint("CK_OnlinePaymentRefunds_Status", "[Status] IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_OnlinePaymentRefunds_OnlineCheckouts_CheckoutId",
                        column: x => x.CheckoutId,
                        principalTable: "OnlineCheckouts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OnlinePaymentRefunds_OrderRefunds_OrderRefundId",
                        column: x => x.OrderRefundId,
                        principalTable: "OrderRefunds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OnlinePaymentRefunds_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OnlinePaymentRefunds_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCheckouts_CustomerUserId",
                table: "OnlineCheckouts",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCheckouts_OrderId",
                table: "OnlineCheckouts",
                column: "OrderId",
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCheckouts_Status_ExpiresAt",
                table: "OnlineCheckouts",
                columns: new[] { "Status", "ExpiresAt" },
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCheckouts_TenantId_CustomerPhone",
                table: "OnlineCheckouts",
                columns: new[] { "TenantId", "CustomerPhone" },
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentAccounts_ProviderUserId",
                table: "OnlinePaymentAccounts",
                column: "ProviderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentAccounts_TenantId",
                table: "OnlinePaymentAccounts",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentAuthorizations_ExpiresAt",
                table: "OnlinePaymentAuthorizations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentAuthorizations_TenantId",
                table: "OnlinePaymentAuthorizations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentAuthorizations_UserId",
                table: "OnlinePaymentAuthorizations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentRefunds_CheckoutId",
                table: "OnlinePaymentRefunds",
                column: "CheckoutId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentRefunds_OrderId",
                table: "OnlinePaymentRefunds",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentRefunds_OrderRefundId",
                table: "OnlinePaymentRefunds",
                column: "OrderRefundId",
                unique: true,
                filter: "[OrderRefundId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePaymentRefunds_TenantId_PaymentId",
                table: "OnlinePaymentRefunds",
                columns: new[] { "TenantId", "PaymentId" },
                unique: true,
                filter: "[OrderRefundId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back deletes the record of what customers paid online, and of the money that went back to them:
            // once there is one checkout there is only roll-forward. The connected accounts alone can go (the businesses
            // connect them again), and with no checkout no day of the sales report has anything paid online. A missing
            // table compiles (deferred name resolution): a second run of rollback.sql skips it.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [OnlineCheckouts]) OR EXISTS (SELECT 1 FROM [OnlinePaymentRefunds])
    THROW 50024, N'Customers paid online; rolling back would delete the record of their payments and refunds. Roll forward instead.', 1;
");

            migrationBuilder.DropTable(
                name: "OnlinePaymentAccounts");

            migrationBuilder.DropTable(
                name: "OnlinePaymentAuthorizations");

            migrationBuilder.DropTable(
                name: "OnlinePaymentRefunds");

            migrationBuilder.DropTable(
                name: "OnlineCheckouts");

            migrationBuilder.DropColumn(
                name: "OnlineAmount",
                table: "DailySalesSnapshots");
        }
    }
}
