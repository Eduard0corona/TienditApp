using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDecimalQuantitiesAndCfdiData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SatProductCode",
                table: "Products",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatUnitCode",
                table: "Products",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxObject",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "OrderRefundLines",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReturnedQuantity",
                table: "OrderItems",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "OrderItems",
                type: "decimal(18,3)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "SatProductCode",
                table: "OrderItems",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "01010101");

            migrationBuilder.AddColumn<string>(
                name: "SatUnitCode",
                table: "OrderItems",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "H87");

            migrationBuilder.AddColumn<int>(
                name: "TaxObject",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "OrderItems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unit");

            // Lines sold before this migration take the current unit of their product (the best record of how
            // they were sold) and that unit's CFDI code; lines of deleted products stay as pieces (H87).
            migrationBuilder.Sql("""
                EXEC(N'UPDATE [oi]
                SET [Unit] = [p].[Unit],
                    [SatUnitCode] = CASE [p].[Unit]
                        WHEN N''Pack'' THEN N''XPK''
                        WHEN N''Box'' THEN N''XBX''
                        WHEN N''Pair'' THEN N''PR''
                        WHEN N''Kg'' THEN N''KGM''
                        WHEN N''G'' THEN N''GRM''
                        WHEN N''Lb'' THEN N''LBR''
                        WHEN N''Oz'' THEN N''ONZ''
                        WHEN N''Liter'' THEN N''LTR''
                        WHEN N''Ml'' THEN N''MLT''
                        WHEN N''Meter'' THEN N''MTR''
                        WHEN N''Roll'' THEN N''XRO''
                        WHEN N''Hour'' THEN N''HUR''
                        WHEN N''Session'' THEN N''E48''
                        WHEN N''Service'' THEN N''E48''
                        ELSE N''H87''
                    END
                FROM [OrderItems] AS [oi]
                INNER JOIN [Products] AS [p] ON [p].[Id] = [oi].[ProductId]
                WHERE [p].[Unit] IN (N''Unit'', N''Pack'', N''Box'', N''Pair'', N''Kg'', N''G'', N''Lb'', N''Oz'',
                    N''Liter'', N''Ml'', N''Portion'', N''Slice'', N''Serving'', N''Meter'', N''Roll'',
                    N''Hour'', N''Session'', N''Service'')');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Going back to whole quantities would silently truncate weighed or measured sales.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [OrderItems] WHERE [Quantity] <> FLOOR([Quantity]) OR [ReturnedQuantity] <> FLOOR([ReturnedQuantity]))
                    OR EXISTS (SELECT 1 FROM [OrderRefundLines] WHERE [Quantity] <> FLOOR([Quantity]))
                    THROW 50001, N'Hay ventas con cantidades decimales: revertir esta migración las truncaría.', 1;
                """);

            migrationBuilder.DropColumn(
                name: "SatProductCode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SatUnitCode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TaxObject",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SatProductCode",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SatUnitCode",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TaxObject",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "OrderItems");

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "OrderRefundLines",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AlterColumn<int>(
                name: "ReturnedQuantity",
                table: "OrderItems",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "OrderItems",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)");
        }
    }
}
