-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22A, 20261012204959_AddBranchServiceMode,
-- hasta la migración anterior, 20261012145908_AddPlatformSettlements.
-- Generado el 2026-10-12 con `dotnet ef migrations script
-- 20261012204959_AddBranchServiceMode 20261012145908_AddPlatformSettlements
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Se detiene sin cambiar nada (error 50011; con -b, sqlcmd termina ahí) si alguna sucursal es
-- «Solo a domicilio»: sin la columna todas vuelven a ser locales abiertos, y la versión anterior
-- publica la dirección de la principal en el menú en línea (en una cocina sin local, muchas veces
-- una casa). Antes, cámbialas a local y borra su calle en Configuración › Sucursales, o corrige
-- hacia adelante (la consulta 3 de verify.sql dice cuáles son).
-- Sin sucursales así, quita la restricción CK_Branches_ServiceMode y la columna [ServiceMode] de
-- [Branches]. Nada más cambia.
-- Volver al código anterior (master con la Fase 22G) funciona también sin este script: no conoce
-- la columna (ver el aviso de apply.sql sobre la dirección de la principal).
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
    WHERE [MigrationId] = N'20261012204959_AddBranchServiceMode'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [Branches] WHERE [ServiceMode] <> 0)
        THROW 50011, N'There are branches that only deliver; rolling back would publish the address of the main one. Change them to premises (and clear their street) first, or roll forward.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012204959_AddBranchServiceMode'
)
BEGIN
    ALTER TABLE [Branches] DROP CONSTRAINT [CK_Branches_ServiceMode];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012204959_AddBranchServiceMode'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Branches]') AND [c].[name] = N'ServiceMode');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Branches] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Branches] DROP COLUMN [ServiceMode];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012204959_AddBranchServiceMode'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012204959_AddBranchServiceMode';
END;

COMMIT;
GO

