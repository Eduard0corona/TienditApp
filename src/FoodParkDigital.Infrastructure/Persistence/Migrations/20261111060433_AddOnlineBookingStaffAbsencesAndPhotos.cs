using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineBookingStaffAbsencesAndPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "OnlineBookingStaff",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OnlineBookingStaffAbsences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstDay = table.Column<DateOnly>(type: "date", nullable: false),
                    LastDay = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineBookingStaffAbsences", x => x.Id);
                    table.CheckConstraint("CK_OnlineBookingStaffAbsences_Days", "[LastDay] >= [FirstDay]");
                    table.ForeignKey(
                        name: "FK_OnlineBookingStaffAbsences_OnlineBookingStaff_StaffId",
                        column: x => x.StaffId,
                        principalTable: "OnlineBookingStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OnlineBookingStaffAbsences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineBookingStaffAbsences_StaffId_LastDay",
                table: "OnlineBookingStaffAbsences",
                columns: new[] { "StaffId", "LastDay" });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineBookingStaffAbsences_TenantId",
                table: "OnlineBookingStaffAbsences",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 31I (ADR 0089): rolling back would lose the absences and the photos the businesses set. Once one did, it
            // stops before anything changes; query 3 of verify.sql counts them. Without any, it drops only what Up added.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OnlineBookingStaffAbsences] WITH (TABLOCKX, HOLDLOCK))
    OR EXISTS (SELECT 1 FROM [OnlineBookingStaff] WITH (TABLOCKX, HOLDLOCK) WHERE [PhotoUrl] IS NOT NULL)
    THROW 50059, N''A business set an absence or a photo of who attends; rolling back would lose them (query 3 of verify.sql).'', 1;');
");

            migrationBuilder.DropTable(
                name: "OnlineBookingStaffAbsences");

            migrationBuilder.DropColumn(
                name: "PhotoUrl",
                table: "OnlineBookingStaff");
        }
    }
}
