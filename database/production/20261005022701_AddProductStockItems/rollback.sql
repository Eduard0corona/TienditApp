-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261005022701_AddProductStockItems.
-- Generado el 2026-10-05 con `dotnet ef migrations script
-- 20261005022701_AddProductStockItems 20261005003444_AddPaymentTipAndCashReceived
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Quita SupplyItems.[ProductId], su llave y sus índices. Los artículos que eran la existencia de
-- un producto se quedan como insumos, con sus existencias, sus movimientos y la receta de 1 a 1
-- que los descuenta en cada venta: el código anterior los sigue moviendo igual, pero la ficha
-- del producto vuelve a mostrar Products.[StockQuantity]. La consulta 3 de verify.sql cuenta
-- esos artículos.
-- Idempotente: no hace nada si la migración no está aplicada.
--
-- Ejecútalo solo después de volver a desplegar el código anterior, y revierte primero las
-- migraciones posteriores (cada carpeta tiene su rollback.sql), de la más nueva a la más vieja.
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -i rollback.sql
-- =====================================================================================
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    ALTER TABLE [SupplyItems] DROP CONSTRAINT [FK_SupplyItems_Products_ProductId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    DROP INDEX [IX_SupplyItems_ProductId] ON [SupplyItems];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    DROP INDEX [IX_SupplyItems_TenantId_ProductId] ON [SupplyItems];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SupplyItems]') AND [c].[name] = N'ProductId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [SupplyItems] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [SupplyItems] DROP COLUMN [ProductId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems';
END;

COMMIT;
GO
