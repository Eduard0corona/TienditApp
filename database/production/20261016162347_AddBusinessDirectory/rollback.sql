-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24A, 20261016162347_AddBusinessDirectory,
-- hasta la migración anterior, 20261013191311_AddHomeVisits.
-- Generado el 2026-10-16 con `dotnet ef migrations script
-- 20261016162347_AddBusinessDirectory 20261013191311_AddHomeVisits
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Se detiene sin cambiar nada (error 50016; con -b, sqlcmd termina ahí) en cuanto algún dueño
-- decidió cómo se ve su negocio en Explorar: si alguno lo ocultó, escribió su descripción o
-- cambió su categoría (cada cambio queda en la bitácora como DirectoryProfileUpdated). Se
-- perdería su decisión y, al aplicar de nuevo, un negocio oculto volvería a aparecer. Con eso solo
-- queda corregir hacia adelante (la consulta 5 de verify.sql dice quiénes).
-- Sin eso, borra las dos CHECK de [Tenants] y las tres columnas con sus DEFAULT
-- ([TenantSettings].[ListedInDirectory], [Tenants].[DirectoryCategory] y
-- [Tenants].[PublicDescription]). Nada más cambia.
-- Volver al código anterior (master con la Fase 23A) funciona también sin este script: no conoce
-- las columnas.
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
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [TenantSettings] WHERE [ListedInDirectory] = 0)
        OR EXISTS (SELECT 1 FROM [Tenants] WHERE [PublicDescription] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [AuditLogs] WHERE [Action] = N''DirectoryProfileUpdated'')
        THROW 50016, N''Owners already decided how their businesses show in Explorar; rolling back would lose it and list again those they hid. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    ALTER TABLE [Tenants] DROP CONSTRAINT [CK_Tenants_DirectoryCategory];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    ALTER TABLE [Tenants] DROP CONSTRAINT [CK_Tenants_PublicDescription];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'ListedInDirectory');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [ListedInDirectory];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Tenants]') AND [c].[name] = N'DirectoryCategory');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Tenants] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Tenants] DROP COLUMN [DirectoryCategory];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Tenants]') AND [c].[name] = N'PublicDescription');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Tenants] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Tenants] DROP COLUMN [PublicDescription];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory';
END;

COMMIT;
GO
