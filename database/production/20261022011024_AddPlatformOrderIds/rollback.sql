-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22I, 20261022011024_AddPlatformOrderIds,
-- hasta la migración anterior, 20261021040954_AddInventoryTransfers.
-- Generado el 2026-10-22 con `dotnet ef migrations script
-- 20261022011024_AddPlatformOrderIds 20261021040954_AddInventoryTransfers
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50032; con -b, sqlcmd termina ahí) si algún pedido ya tiene
-- su ID largo: se borraría lo que el negocio escribió para encontrar sus pedidos en el estado de
-- cuenta, y nada podría volver a escribirlo. Entonces solo queda corregir hacia adelante (la
-- consulta 3 de verify.sql los cuenta).
-- Sin ellos, borra el índice [IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderId], la
-- restricción [CK_PlatformOrderDetails_ExternalOrderId] y la columna [ExternalOrderId], vacía.
-- Volver al código anterior (master con la Fase 22H) funciona también sin este script: no conoce
-- la columna.
-- Idempotente: no hace nada si la migración no está aplicada.
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
    WHERE [MigrationId] = N'20261022011024_AddPlatformOrderIds'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [PlatformOrderDetails] WHERE [ExternalOrderId] IS NOT NULL)
        THROW 50032, N''Businesses wrote the long id of orders of the delivery platforms already; rolling back would erase them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022011024_AddPlatformOrderIds'
)
BEGIN
    DROP INDEX [IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderId] ON [PlatformOrderDetails];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022011024_AddPlatformOrderIds'
)
BEGIN
    ALTER TABLE [PlatformOrderDetails] DROP CONSTRAINT [CK_PlatformOrderDetails_ExternalOrderId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022011024_AddPlatformOrderIds'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PlatformOrderDetails]') AND [c].[name] = N'ExternalOrderId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [PlatformOrderDetails] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [PlatformOrderDetails] DROP COLUMN [ExternalOrderId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022011024_AddPlatformOrderIds'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022011024_AddPlatformOrderIds';
END;

COMMIT;
GO
