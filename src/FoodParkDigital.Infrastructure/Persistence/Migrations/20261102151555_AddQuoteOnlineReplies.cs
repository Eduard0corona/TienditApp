using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteOnlineReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Quotes_Reply",
                table: "Quotes");

            migrationBuilder.AddColumn<bool>(
                name: "QuoteOnlineReplies",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Quotes_Reply",
                table: "Quotes",
                sql: "([ReplyChannel] IS NULL AND [CustomerResponse] IS NULL AND [Status] NOT IN (2, 3)) OR ([ReplyChannel] IN (0, 1, 2, 3, 4) AND [Status] IN (2, 3, 5))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An answer from the link would not fit the previous rule of the answers, and a business that turned those answers
            // off would get them back when this migration is applied again (Fase 29G).
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [Quotes] WHERE [ReplyChannel] = 4)
    THROW 50055, N''Customers already answered quotes from their link; rolling back would not keep how they answered. Roll forward instead.'', 1;
IF EXISTS (SELECT 1 FROM [TenantSettings] WHERE [QuoteOnlineReplies] = 0)
    THROW 50055, N''A business turned off the answers to its quotes from their link; rolling back would forget it and turn them on again when rolled forward. Roll forward instead.'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Quotes_Reply",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "QuoteOnlineReplies",
                table: "TenantSettings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Quotes_Reply",
                table: "Quotes",
                sql: "([ReplyChannel] IS NULL AND [CustomerResponse] IS NULL AND [Status] NOT IN (2, 3)) OR ([ReplyChannel] IN (0, 1, 2, 3) AND [Status] IN (2, 3, 5))");
        }
    }
}
