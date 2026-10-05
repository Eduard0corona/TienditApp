-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261005045624_AddPurchaseOrderClosing.
-- Generado el 2026-10-05 con `dotnet ef migrations script
-- 20261005045624_AddPurchaseOrderClosing 20261005022701_AddProductStockItems
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Quita PurchaseOrders.[ClosedAt] y [CloseReason]. Si alguna orden ya se cerró sin el resto
-- (estado 4), se detiene con el error 50007 sin cambiar nada: el código anterior no conoce ese
-- estado y la mostraría como pendiente, y se perdería cuándo y por qué se cerró. La consulta 3 de
-- verify.sql las cuenta; para revertir de todos modos hay que decidir antes, con cada negocio,
-- qué estado darles en el código anterior (este script no lo hace).
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
    WHERE [MigrationId] = N'20261005045624_AddPurchaseOrderClosing'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [PurchaseOrders] WHERE [Status] = 4)
        THROW 50007, N'There are purchase orders closed without the remainder; rolling back would lose why and when they were closed.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005045624_AddPurchaseOrderClosing'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseOrders]') AND [c].[name] = N'CloseReason');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [PurchaseOrders] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [PurchaseOrders] DROP COLUMN [CloseReason];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005045624_AddPurchaseOrderClosing'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseOrders]') AND [c].[name] = N'ClosedAt');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [PurchaseOrders] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [PurchaseOrders] DROP COLUMN [ClosedAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005045624_AddPurchaseOrderClosing'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005045624_AddPurchaseOrderClosing';
END;

COMMIT;
GO
