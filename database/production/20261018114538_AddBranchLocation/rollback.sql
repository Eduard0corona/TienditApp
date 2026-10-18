-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24G, 20261018114538_AddBranchLocation,
-- hasta la migración anterior, 20261018081803_AddFavoriteBusinesses.
-- Generado el 2026-10-18 con `dotnet ef migrations script
-- 20261018114538_AddBranchLocation 20261018081803_AddFavoriteBusinesses
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50022; con -b, sqlcmd termina ahí) si algún dueño ya puso
-- una sucursal en el mapa: revertir olvidaría dónde está. Entonces solo queda corregir hacia
-- adelante (la consulta 3 de verify.sql las cuenta). Sin ubicaciones, quita la restricción
-- [CK_Branches_Location] y las columnas [Branches].[Latitude] y [Longitude]. Nada más cambia.
-- Volver al código anterior (master con la Fase 24F) funciona también sin este script: no conoce
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
    WHERE [MigrationId] = N'20261018114538_AddBranchLocation'
)
BEGIN

    IF COL_LENGTH(N'dbo.Branches', N'Latitude') IS NOT NULL
        EXEC(N'IF EXISTS (SELECT 1 FROM [Branches] WHERE [Latitude] IS NOT NULL)
        THROW 50022, N''Owners put branches on the map; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018114538_AddBranchLocation'
)
BEGIN
    ALTER TABLE [Branches] DROP CONSTRAINT [CK_Branches_Location];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018114538_AddBranchLocation'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Branches]') AND [c].[name] = N'Latitude');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Branches] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Branches] DROP COLUMN [Latitude];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018114538_AddBranchLocation'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Branches]') AND [c].[name] = N'Longitude');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Branches] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Branches] DROP COLUMN [Longitude];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018114538_AddBranchLocation'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018114538_AddBranchLocation';
END;

COMMIT;
GO
