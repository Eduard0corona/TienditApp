using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCreditReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreditReminderEveryDays",
                table: "TenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<Guid>(
                name: "CreditReminderTemplateId",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CreditRemindersEnabled",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CreditTermDays",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_CreditReminderEveryDays",
                table: "TenantSettings",
                sql: "[CreditReminderEveryDays] >= 1 AND [CreditReminderEveryDays] <= 30");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_CreditTermDays",
                table: "Customers",
                sql: "[CreditTermDays] IS NULL OR ([CreditTermDays] >= 1 AND [CreditTermDays] <= 120)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A customer's term decides what of theirs is overdue, and the reminders and their templates and messages are the
            // business's word on how to ask for it: rolling back would forget the terms and leave reminders the previous
            // version cannot read, so once a business uses any of it there is only roll-forward. The check runs as dynamic
            // SQL: a second run of rollback.sql, with the columns gone, skips it without compiling it.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [Customers] WHERE [CreditTermDays] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [TenantSettings] WHERE [CreditRemindersEnabled] = 1 OR [CreditReminderTemplateId] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [MessageTemplates] WHERE [Purpose] = 3)
    OR EXISTS (SELECT 1 FROM [OutgoingMessages] WHERE [Purpose] = 3)
    THROW 50028, N''Businesses gave customers their own credit term or used credit reminders already; rolling back would forget the terms and leave reminders the previous version cannot read. Roll forward instead.'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_CreditReminderEveryDays",
                table: "TenantSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_CreditTermDays",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CreditReminderEveryDays",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CreditReminderTemplateId",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CreditRemindersEnabled",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CreditTermDays",
                table: "Customers");
        }
    }
}
