-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004160303_AddBranchesAndDailySalesSnapshots.
-- Generado el 2026-10-04 con `dotnet ef migrations script
-- 20261004160303_AddBranchesAndDailySalesSnapshots 20261004153003_AddOnlineMenu
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra Branches, DailySalesSnapshots (el worker los vuelve a calcular) y las columnas
-- [BranchId]; las ubicaciones de inventario se conservan. Si hay sucursales que no venían de
-- una ubicación (creadas después, o la "Sucursal principal" ya editada), el script se detiene
-- sin cambiar nada (error 50003): la consulta 6 de verify.sql las lista para resolverlas antes.
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
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN

    IF EXISTS (
        SELECT 1 FROM [Branches] b
        WHERE NOT EXISTS (SELECT 1 FROM [InventoryLocations] l WHERE l.[Id] = b.[Id])
          AND NOT (b.[IsDefault] = 1 AND b.[Code] = N'PRINCIPAL' AND b.[Name] = N'Sucursal principal'
              AND b.[Description] IS NULL AND b.[AddressLine1] IS NULL AND b.[AddressLine2] IS NULL AND b.[City] IS NULL
              AND b.[State] IS NULL AND b.[Country] IS NULL AND b.[PostalCode] IS NULL
              AND b.[ContactName] IS NULL AND b.[ContactPhone] IS NULL))
        THROW 50003, N'There are branches that were not inventory locations; rolling back would lose them.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    ALTER TABLE [CashSessions] DROP CONSTRAINT [FK_CashSessions_Branches_BranchId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    ALTER TABLE [InventoryLocations] DROP CONSTRAINT [FK_InventoryLocations_Branches_BranchId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    ALTER TABLE [Orders] DROP CONSTRAINT [FK_Orders_Branches_BranchId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DROP TABLE [Branches];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DROP TABLE [DailySalesSnapshots];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DROP INDEX [IX_Orders_BranchId] ON [Orders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DROP INDEX [IX_Orders_TenantId_BranchId_CreatedAt] ON [Orders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DROP INDEX [IX_InventoryLocations_BranchId] ON [InventoryLocations];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DROP INDEX [IX_InventoryLocations_TenantId_BranchId] ON [InventoryLocations];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DROP INDEX [IX_CashSessions_BranchId] ON [CashSessions];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DROP INDEX [IX_CashSessions_TenantId_BranchId_OpenedAt] ON [CashSessions];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'BranchId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Orders] DROP COLUMN [BranchId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InventoryLocations]') AND [c].[name] = N'BranchId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [InventoryLocations] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [InventoryLocations] DROP COLUMN [BranchId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CashSessions]') AND [c].[name] = N'BranchId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [CashSessions] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [CashSessions] DROP COLUMN [BranchId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots';
END;

COMMIT;
GO
