-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24E, 20261018032200_AddBusinessCover,
-- hasta la migración anterior, 20261017225015_AddOnlineBooking.
-- Generado el 2026-10-18 con `dotnet ef migrations script
-- 20261018032200_AddBusinessCover 20261017225015_AddOnlineBooking
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Quita la columna [Tenants].[CoverImageUrl] solo si ningún dueño subió una portada. En cuanto
-- existe una, se detiene con THROW 50020 sin cambiar nada: revertir olvidaría la portada de esos
-- negocios y dejaría sus archivos en el almacenamiento sin nada que los use. Entonces solo queda
-- avanzar (la consulta 3 de verify.sql dice cuántos negocios la tienen). Antes de revertir,
-- considera volver solo al código anterior, que funciona sin este script.
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
    WHERE [MigrationId] = N'20261018032200_AddBusinessCover'
)
BEGIN

    IF COL_LENGTH(N'dbo.Tenants', N'CoverImageUrl') IS NOT NULL
        EXEC(N'IF EXISTS (SELECT 1 FROM [Tenants] WHERE [CoverImageUrl] IS NOT NULL)
        THROW 50020, N''Owners uploaded menu covers; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018032200_AddBusinessCover'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Tenants]') AND [c].[name] = N'CoverImageUrl');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Tenants] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Tenants] DROP COLUMN [CoverImageUrl];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018032200_AddBusinessCover'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018032200_AddBusinessCover';
END;

COMMIT;
GO

