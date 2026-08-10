using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceBoardAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignedAt",
                table: "ServiceOrderPieces",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedToName",
                table: "ServiceOrderPieces",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "ServiceOrderPieces",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderPieces_AssignedToUserId",
                table: "ServiceOrderPieces",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderPieces_TenantId_AssignedToUserId_Status",
                table: "ServiceOrderPieces",
                columns: new[] { "TenantId", "AssignedToUserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceOrderPieces_AspNetUsers_AssignedToUserId",
                table: "ServiceOrderPieces",
                column: "AssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceOrderPieces_AspNetUsers_AssignedToUserId",
                table: "ServiceOrderPieces");

            migrationBuilder.DropIndex(
                name: "IX_ServiceOrderPieces_AssignedToUserId",
                table: "ServiceOrderPieces");

            migrationBuilder.DropIndex(
                name: "IX_ServiceOrderPieces_TenantId_AssignedToUserId_Status",
                table: "ServiceOrderPieces");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "ServiceOrderPieces");

            migrationBuilder.DropColumn(
                name: "AssignedToName",
                table: "ServiceOrderPieces");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "ServiceOrderPieces");
        }
    }
}
