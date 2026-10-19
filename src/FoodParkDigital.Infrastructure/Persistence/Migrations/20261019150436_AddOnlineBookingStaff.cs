using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineBookingStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnlineBookingStaff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    AllServices = table.Column<bool>(type: "bit", nullable: false),
                    WeeklyHours = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineBookingStaff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineBookingStaff_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OnlineBookingStaff_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OnlineBookingStaffServices",
                columns: table => new
                {
                    StaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineBookingStaffServices", x => new { x.StaffId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_OnlineBookingStaffServices_OnlineBookingStaff_StaffId",
                        column: x => x.StaffId,
                        principalTable: "OnlineBookingStaff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OnlineBookingStaffServices_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OnlineBookingStaffServices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineBookingStaff_TenantId_UserId",
                table: "OnlineBookingStaff",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlineBookingStaff_UserId",
                table: "OnlineBookingStaff",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineBookingStaffServices_ProductId",
                table: "OnlineBookingStaffServices",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineBookingStaffServices_TenantId",
                table: "OnlineBookingStaffServices",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back deletes who each business takes online appointments for, their services and their hours: once
            // a business set someone there is only roll-forward. The appointments keep who attends them either way. A
            // missing table compiles (deferred name resolution): a second run of rollback.sql skips it.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [OnlineBookingStaff])
    THROW 50025, N'Businesses set who takes their online appointments; rolling back would delete it. Roll forward instead.', 1;
");

            migrationBuilder.DropTable(
                name: "OnlineBookingStaffServices");

            migrationBuilder.DropTable(
                name: "OnlineBookingStaff");
        }
    }
}
