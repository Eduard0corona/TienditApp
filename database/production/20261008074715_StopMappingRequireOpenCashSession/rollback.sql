-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 15, 20261008074715_StopMappingRequireOpenCashSession y
-- 20261008072542_AddTabItemCancellations, hasta 20261006201255_DropStockAndLoyaltyColumns.
-- Generado el 2026-10-08 con `dotnet ef migrations script
-- 20261008074715_StopMappingRequireOpenCashSession 20261006201255_DropStockAndLoyaltyColumns
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra [OrderItemCancellations] y las columnas [OrderItems].[CancelledQuantity] y
-- [CancelledAmount]: los registros de platillos cancelados se pierden (la bitácora conserva
-- cada cancelación con su motivo, y el respaldo de la base todo). Los totales guardados de las
-- cuentas no cambian. [TenantSettings].[RequireOpenCashSession] no se toca: sigue ahí.
-- Solo hace falta para volver a la Fase 14 con la base como estaba; volver al código de la Fase
-- 14 funciona también sin este script. En los dos casos, antes cierra o cancela las cuentas
-- abiertas con platillos cancelados (deploy-azure.md). La consulta 4 de verify.sql las cuenta.
-- Idempotente: no hace nada si las migraciones no están aplicadas.
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
    WHERE [MigrationId] = N'20261008074715_StopMappingRequireOpenCashSession'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008074715_StopMappingRequireOpenCashSession';
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    DROP TABLE [OrderItemCancellations];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderItems]') AND [c].[name] = N'CancelledAmount');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OrderItems] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OrderItems] DROP COLUMN [CancelledAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderItems]') AND [c].[name] = N'CancelledQuantity');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OrderItems] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OrderItems] DROP COLUMN [CancelledQuantity];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations';
END;

COMMIT;
GO
