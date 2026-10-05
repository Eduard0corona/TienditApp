using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Order lines and status history get the business of their order, so the tenant filter applies to them too.
    /// Applied while the previous version runs: the lines and history it adds meanwhile take the empty default, and the
    /// new version gives them the business of their order when it starts (OrderTenantBackfillService).
    /// </summary>
    public partial class ScopeOrderLinesAndHistoryByTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "OrderStatusHistory",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "OrderItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // What already exists belongs to the business of its order. Through EXEC because the idempotent script runs
            // each migration as one batch, and SQL Server compiles a batch before the column exists.
            migrationBuilder.Sql("""
                EXEC(N'UPDATE i SET i.[TenantId] = o.[TenantId]
                FROM [OrderItems] AS i
                INNER JOIN [Orders] AS o ON o.[Id] = i.[OrderId]
                WHERE i.[TenantId] = ''00000000-0000-0000-0000-000000000000''');
                """);
            migrationBuilder.Sql("""
                EXEC(N'UPDATE h SET h.[TenantId] = o.[TenantId]
                FROM [OrderStatusHistory] AS h
                INNER JOIN [Orders] AS o ON o.[Id] = h.[OrderId]
                WHERE h.[TenantId] = ''00000000-0000-0000-0000-000000000000''');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistory_TenantId_OrderId",
                table: "OrderStatusHistory",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_TenantId_OrderId",
                table: "OrderItems",
                columns: new[] { "TenantId", "OrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderStatusHistory_TenantId_OrderId",
                table: "OrderStatusHistory");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_TenantId_OrderId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "OrderStatusHistory");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "OrderItems");
        }
    }
}
