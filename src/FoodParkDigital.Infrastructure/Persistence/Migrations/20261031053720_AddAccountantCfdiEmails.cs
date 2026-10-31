using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountantCfdiEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountantEmail",
                table: "InvoicingProfiles",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SendsMonthToAccountant",
                table: "InvoicingProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CfdiMonthEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    To = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LastFailedAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CfdiCount = table.Column<int>(type: "int", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Failure = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CfdiMonthEmails", x => x.Id);
                    table.CheckConstraint("CK_CfdiMonthEmails_Attempts", "([FailedAttempts] = 0 AND [LastFailedAttemptAt] IS NULL) OR ([FailedAttempts] > 0 AND [LastFailedAttemptAt] IS NOT NULL)");
                    table.CheckConstraint("CK_CfdiMonthEmails_Month", "[Year] >= 2022 AND [Month] BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_CfdiMonthEmails_Outcome", "([SentAt] IS NULL OR [FailedAt] IS NULL) AND (([SentAt] IS NULL AND [CfdiCount] IS NULL) OR ([SentAt] IS NOT NULL AND [CfdiCount] IS NOT NULL AND [CfdiCount] >= 0)) AND (([FailedAt] IS NULL AND [Failure] IS NULL) OR ([FailedAt] IS NOT NULL AND [Failure] IS NOT NULL AND [Failure] IN (0, 1, 2, 3, 4)))");
                    table.ForeignKey(
                        name: "FK_CfdiMonthEmails_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoicingProfiles_Accountant",
                table: "InvoicingProfiles",
                sql: "[SendsMonthToAccountant] = 0 OR [AccountantEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CfdiMonthEmails_TenantId_RequestedAt",
                table: "CfdiMonthEmails",
                columns: new[] { "TenantId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CfdiMonthEmails_TenantId_Year_Month_RequestedAt",
                table: "CfdiMonthEmails",
                columns: new[] { "TenantId", "Year", "Month", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_CfdiMonthEmails_Monthly",
                table: "CfdiMonthEmails",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true,
                filter: "[RequestedByUserId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Going back would delete the history of what went to the accountant: stop.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [CfdiMonthEmails])
    THROW 50048, N''Emails of CFDI to the accountant are recorded already; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "CfdiMonthEmails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoicingProfiles_Accountant",
                table: "InvoicingProfiles");

            migrationBuilder.DropColumn(
                name: "AccountantEmail",
                table: "InvoicingProfiles");

            migrationBuilder.DropColumn(
                name: "SendsMonthToAccountant",
                table: "InvoicingProfiles");
        }
    }
}
