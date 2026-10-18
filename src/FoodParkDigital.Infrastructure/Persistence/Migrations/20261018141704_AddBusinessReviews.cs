using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WrittenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reply = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RepliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReportReason = table.Column<int>(type: "int", nullable: true),
                    HiddenReason = table.Column<int>(type: "int", nullable: true),
                    ModeratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessReviews", x => x.Id);
                    table.CheckConstraint("CK_BusinessReviews_Hidden", "([Status] = 0 AND [HiddenReason] IS NULL) OR ([Status] = 1 AND [HiddenReason] IS NOT NULL AND [HiddenReason] BETWEEN 1 AND 5)");
                    table.CheckConstraint("CK_BusinessReviews_Rating", "[Rating] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_BusinessReviews_Reply", "([Reply] IS NULL AND [RepliedAt] IS NULL) OR ([Reply] IS NOT NULL AND [RepliedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_BusinessReviews_Report", "([ReportedAt] IS NULL AND [ReportReason] IS NULL) OR ([ReportedAt] IS NOT NULL AND [ReportReason] IS NOT NULL AND [ReportReason] BETWEEN 1 AND 5 AND [Status] = 0)");
                    table.CheckConstraint("CK_BusinessReviews_Status", "[Status] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_BusinessReviews_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BusinessReviews_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessReviews_ReportedAt",
                table: "BusinessReviews",
                column: "ReportedAt",
                filter: "[ReportedAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessReviews_TenantId_Status_WrittenAt",
                table: "BusinessReviews",
                columns: new[] { "TenantId", "Status", "WrittenAt" })
                .Annotation("SqlServer:Include", new[] { "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessReviews_UserId_TenantId",
                table: "BusinessReviews",
                columns: new[] { "UserId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back deletes what customers wrote about the businesses they bought from, and the businesses'
            // answers: once there is one review there is only roll-forward. A missing table compiles (deferred name
            // resolution): a second run of rollback.sql skips it.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [BusinessReviews])
    THROW 50023, N'Customers reviewed businesses; rolling back would delete their reviews. Roll forward instead.', 1;
");

            migrationBuilder.DropTable(
                name: "BusinessReviews");
        }
    }
}
