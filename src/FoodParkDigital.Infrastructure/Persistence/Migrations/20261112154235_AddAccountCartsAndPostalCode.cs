using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCartsAndPostalCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "AspNetUsers",
                type: "varchar(5)",
                unicode: false,
                maxLength: 5,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountCarts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReorderOf = table.Column<int>(type: "int", nullable: true),
                    Skipped = table.Column<int>(type: "int", nullable: true),
                    SavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Lines = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountCarts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountCarts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountCarts_Tenants_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountCarts_BusinessId",
                table: "AccountCarts",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountCarts_UserId_BusinessId",
                table: "AccountCarts",
                columns: new[] { "UserId", "BusinessId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 31J (ADR 0090): rolling back would lose the carts and the postal codes the customer accounts left. Once one
            // did, it stops before anything changes; query 3 of verify.sql counts them. Without any, it drops only what Up added.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [AccountCarts] WITH (TABLOCKX, HOLDLOCK))
    OR EXISTS (SELECT 1 FROM [AspNetUsers] WITH (TABLOCKX, HOLDLOCK) WHERE [PostalCode] IS NOT NULL)
    THROW 50060, N''A customer account left a cart or a postal code; rolling back would lose them (query 3 of verify.sql).'', 1;');
");

            migrationBuilder.DropTable(
                name: "AccountCarts");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "AspNetUsers");
        }
    }
}
