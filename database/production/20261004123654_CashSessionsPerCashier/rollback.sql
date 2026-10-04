-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004123654_CashSessionsPerCashier.
-- Generado el 2026-10-04 con `dotnet ef migrations script
-- 20261004123654_CashSessionsPerCashier 20261004121539_AddOutboxAndWebhookInbox
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Quita las columnas de sucursal, cajero, esperado, diferencia, notas y conteo por
-- denominación, y vuelve al índice de una sola caja abierta por negocio. Se pierde a qué
-- cajero y sucursal pertenece cada caja y las notas y diferencias de cierre. Antes de revertir,
-- cada negocio debe tener como máximo una caja abierta (consulta 6 de verify.sql vacía): si no,
-- el índice único no se puede crear y el script falla sin cambiar nada.
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
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    ALTER TABLE [CashSessions] DROP CONSTRAINT [FK_CashSessions_InventoryLocations_InventoryLocationId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DROP INDEX [IX_CashSessions_InventoryLocationId] ON [CashSessions];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DROP INDEX [IX_CashSessions_TenantId_InventoryLocationId_OpenedAt] ON [CashSessions];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DROP INDEX [IX_CashSessions_TenantId_OpenedByUserId_Open] ON [CashSessions];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'RequireOpenCashSession');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [RequireOpenCashSession];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CashSessions]') AND [c].[name] = N'CashCountJson');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [CashSessions] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [CashSessions] DROP COLUMN [CashCountJson];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CashSessions]') AND [c].[name] = N'ClosingDifference');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [CashSessions] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [CashSessions] DROP COLUMN [ClosingDifference];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CashSessions]') AND [c].[name] = N'ClosingNotes');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [CashSessions] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [CashSessions] DROP COLUMN [ClosingNotes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CashSessions]') AND [c].[name] = N'ExpectedClosingAmount');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [CashSessions] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [CashSessions] DROP COLUMN [ExpectedClosingAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CashSessions]') AND [c].[name] = N'InventoryLocationId');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [CashSessions] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [CashSessions] DROP COLUMN [InventoryLocationId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CashSessions]') AND [c].[name] = N'OpenedByUserId');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [CashSessions] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [CashSessions] DROP COLUMN [OpenedByUserId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CashSessions_TenantId_SingleOpen] ON [CashSessions] ([TenantId]) WHERE [Status] = 0');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier';
END;

COMMIT;
GO
