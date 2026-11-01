using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuoteValidityDays",
                table: "TenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PricesIncludeTax = table.Column<bool>(type: "bit", nullable: false),
                    ValidityDays = table.Column<int>(type: "int", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VisitCreditSaleOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VisitCreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TrackingToken = table.Column<string>(type: "varchar(43)", unicode: false, maxLength: 43, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplyChannel = table.Column<int>(type: "int", nullable: true),
                    CustomerResponse = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.CheckConstraint("CK_Quotes_ClosedAt", "([Status] IN (0, 1) AND [ClosedAt] IS NULL) OR ([Status] IN (2, 3, 5, 6) AND [ClosedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_Quotes_Reply", "([ReplyChannel] IS NULL AND [CustomerResponse] IS NULL AND [Status] NOT IN (2, 3)) OR ([ReplyChannel] IN (0, 1, 2, 3) AND [Status] IN (2, 3, 5))");
                    table.CheckConstraint("CK_Quotes_Revision", "[Revision] >= 1");
                    table.CheckConstraint("CK_Quotes_Sent", "([Status] IN (0, 6) AND [ValidUntil] IS NULL AND [TrackingToken] IS NULL AND [SentAt] IS NULL) OR ([Status] IN (1, 2, 3, 5) AND [ValidUntil] IS NOT NULL AND [TrackingToken] IS NOT NULL AND [SentAt] IS NOT NULL)");
                    table.CheckConstraint("CK_Quotes_Status", "[Status] IN (0, 1, 2, 3, 5, 6)");
                    table.CheckConstraint("CK_Quotes_Totals", "[SubTotal] >= 0 AND [Tax] >= 0 AND [Total] = [SubTotal] + [Tax]");
                    table.CheckConstraint("CK_Quotes_TrackingToken", "[TrackingToken] IS NULL OR LEN([TrackingToken]) = 43");
                    table.CheckConstraint("CK_Quotes_ValidityDays", "[ValidityDays] BETWEEN 1 AND 90");
                    table.CheckConstraint("CK_Quotes_VisitCredit", "([VisitCreditSaleOrderId] IS NULL AND [VisitCreditAmount] IS NULL) OR ([VisitCreditSaleOrderId] IS NOT NULL AND [VisitCreditAmount] > 0 AND [AppointmentId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Quotes_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Quotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Quotes_Orders_VisitCreditSaleOrderId",
                        column: x => x.VisitCreditSaleOrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Quotes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    IsTaxExempt = table.Column<bool>(type: "bit", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLines", x => x.Id);
                    table.CheckConstraint("CK_QuoteLines_Position", "[Position] BETWEEN 1 AND 50");
                    table.CheckConstraint("CK_QuoteLines_Quantity", "[Quantity] > 0 AND [Quantity] <= 10000");
                    table.CheckConstraint("CK_QuoteLines_Tax", "[IsTaxExempt] = 0 OR [TaxRate] = 0");
                    table.CheckConstraint("CK_QuoteLines_UnitPrice", "[UnitPrice] >= 0 AND [UnitPrice] <= 1000000");
                    table.ForeignKey(
                        name: "FK_QuoteLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QuoteLines_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_QuoteValidityDays",
                table: "TenantSettings",
                sql: "[QuoteValidityDays] >= 1 AND [QuoteValidityDays] <= 90");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLines_ProductId",
                table: "QuoteLines",
                column: "ProductId",
                filter: "[ProductId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLines_QuoteId_Position",
                table: "QuoteLines",
                columns: new[] { "QuoteId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_AppointmentId",
                table: "Quotes",
                column: "AppointmentId",
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CustomerId",
                table: "Quotes",
                column: "CustomerId",
                filter: "[CustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_TenantId_CustomerPhone",
                table: "Quotes",
                columns: new[] { "TenantId", "CustomerPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_TenantId_Number_Draft",
                table: "Quotes",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_TenantId_Number_Revision",
                table: "Quotes",
                columns: new[] { "TenantId", "Number", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_TenantId_Status_UpdatedAt",
                table: "Quotes",
                columns: new[] { "TenantId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_TrackingToken",
                table: "Quotes",
                column: "TrackingToken",
                unique: true,
                filter: "[TrackingToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_VisitCreditSaleOrderId",
                table: "Quotes",
                column: "VisitCreditSaleOrderId",
                filter: "[VisitCreditSaleOrderId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back loses the quotes and the validity a business picked for them. Once there is a quote, or a business
            // with a validity other than 15 days, there is only roll-forward. Through EXEC: on a second run of rollback.sql the
            // table and the column are gone, and a batch that names them would not compile.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [Quotes])
    OR EXISTS (SELECT 1 FROM [TenantSettings] WHERE [QuoteValidityDays] <> 15)
    THROW 50051, N''Businesses already have quotes, or picked how long they are valid; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "QuoteLines");

            migrationBuilder.DropTable(
                name: "Quotes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_QuoteValidityDays",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "QuoteValidityDays",
                table: "TenantSettings");
        }
    }
}
