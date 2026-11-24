using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportAccesses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAccesses", x => x.Id);
                    table.CheckConstraint("CK_SupportAccesses_Duration", "[ExpiresAt] > [StartedAt]");
                    table.CheckConstraint("CK_SupportAccesses_EndedAt", "[EndedAt] IS NULL OR ([EndedAt] >= [StartedAt] AND [EndedAt] < [ExpiresAt])");
                    table.CheckConstraint("CK_SupportAccesses_Reason", "LEN([Reason]) BETWEEN 10 AND 500");
                    table.ForeignKey(
                        name: "FK_SupportAccesses_AspNetUsers_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportAccesses_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccesses_AdminUserId_StartedAt",
                table: "SupportAccesses",
                columns: new[] { "AdminUserId", "StartedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccesses_TenantId_AdminUserId_ExpiresAt",
                table: "SupportAccesses",
                columns: new[] { "TenantId", "AdminUserId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 32G (ADR 0099): the accesses are history (who entered which business, why and until when). Once there is
            // one, it stops before anything changes; query 2 of verify.sql counts them. The code before ignores the table, so
            // going back to it needs no rollback.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [SupportAccesses] WITH (TABLOCKX, HOLDLOCK))
    THROW 50066, N''There are support accesses; rolling back would delete their history (query 2 of verify.sql). Go back to the previous code without it.'', 1;');
");

            migrationBuilder.DropTable(
                name: "SupportAccesses");
        }
    }
}
