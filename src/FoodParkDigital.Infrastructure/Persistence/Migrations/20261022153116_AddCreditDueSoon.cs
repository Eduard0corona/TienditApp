using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditDueSoon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreditDueSoonDaysBefore",
                table: "TenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<bool>(
                name: "CreditDueSoonEnabled",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CreditDueSoonTemplateId",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_CreditDueSoonDaysBefore",
                table: "TenantSettings",
                sql: "[CreditDueSoonDaysBefore] >= 1 AND [CreditDueSoonDaysBefore] <= 7");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The notice and its templates and messages are the business's word on how to ask for the money: rolling back would
            // leave notices and templates the previous version cannot read, so once a business uses any of it there is only
            // roll-forward. The check runs as dynamic SQL: a second run of rollback.sql, with the columns gone, skips it
            // without compiling it.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [TenantSettings] WHERE [CreditDueSoonEnabled] = 1 OR [CreditDueSoonTemplateId] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [MessageTemplates] WHERE [Purpose] = 4)
    OR EXISTS (SELECT 1 FROM [OutgoingMessages] WHERE [Purpose] = 4)
    THROW 50034, N''Businesses used the notice before the credit comes due already; rolling back would leave notices and templates the previous version cannot read. Roll forward instead.'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_CreditDueSoonDaysBefore",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CreditDueSoonDaysBefore",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CreditDueSoonEnabled",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CreditDueSoonTemplateId",
                table: "TenantSettings");
        }
    }
}
