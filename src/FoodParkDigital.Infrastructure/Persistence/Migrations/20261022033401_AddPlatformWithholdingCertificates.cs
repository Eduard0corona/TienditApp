using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformWithholdingCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformWithholdingCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuerRfc = table.Column<string>(type: "varchar(13)", unicode: false, maxLength: 13, nullable: false),
                    IssuerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ReceiverRfc = table.Column<string>(type: "varchar(13)", unicode: false, maxLength: 13, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    MonthFrom = table.Column<int>(type: "int", nullable: false),
                    MonthTo = table.Column<int>(type: "int", nullable: false),
                    IssuedOn = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    OperationTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsrBase = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsrWithheld = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatBase = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    VatWithheld = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalWithheld = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemovalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformWithholdingCertificates", x => x.Id);
                    table.CheckConstraint("CK_PlatformWithholdingCertificates_Amounts", "[OperationTotal] >= 0 AND [IsrWithheld] >= 0 AND [VatWithheld] >= 0 AND [TotalWithheld] >= [IsrWithheld] + [VatWithheld] AND ([IsrBase] IS NULL OR [IsrBase] >= 0) AND ([VatBase] IS NULL OR [VatBase] >= 0)");
                    table.CheckConstraint("CK_PlatformWithholdingCertificates_Channel", "[Channel] IN (3, 4, 5)");
                    table.CheckConstraint("CK_PlatformWithholdingCertificates_Period", "[MonthFrom] BETWEEN 1 AND 12 AND [MonthTo] BETWEEN 1 AND 12 AND [MonthFrom] <= [MonthTo] AND [Year] >= 2019");
                    table.CheckConstraint("CK_PlatformWithholdingCertificates_Removed", "([RemovedAt] IS NULL AND [RemovalReason] IS NULL) OR ([RemovedAt] IS NOT NULL AND [RemovalReason] IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformWithholdingCertificates_TenantId_Channel_Year_MonthTo",
                table: "PlatformWithholdingCertificates",
                columns: new[] { "TenantId", "Channel", "Year", "MonthTo" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformWithholdingCertificates_TenantId_Uuid_Active",
                table: "PlatformWithholdingCertificates",
                columns: new[] { "TenantId", "Uuid" },
                unique: true,
                filter: "[RemovedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The CFDI de retenciones the businesses uploaded would be lost: the activity keeps what each said, but the
            // reconciliation would have nothing to compare. With any saved there is only roll-forward, or exporting and
            // deleting them on purpose first. Nothing else changes either way.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [PlatformWithholdingCertificates])
    THROW 50033, N'There are CFDI de retenciones of delivery platforms saved; rolling back would delete them. Export and delete them first, or roll forward.', 1;
");

            migrationBuilder.DropTable(
                name: "PlatformWithholdingCertificates");
        }
    }
}
