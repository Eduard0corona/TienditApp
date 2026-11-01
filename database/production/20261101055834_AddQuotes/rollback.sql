-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29C, 20261101055834_AddQuotes,
-- hasta la migración anterior, 20261101002207_AddHomeVisitBooking.
-- Generado el 2026-11-01 con `dotnet ef migrations script
-- 20261101055834_AddQuotes 20261101002207_AddHomeVisitBooking --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50051; con -b, sqlcmd termina ahí) en cuanto un negocio tiene
-- una cotización (aunque sea un borrador) o eligió una vigencia distinta de 15 días: se perderían.
-- Entonces solo queda corregir hacia adelante (la consulta 3 de verify.sql los cuenta). Si no,
-- borra las dos tablas nuevas, la restricción y la columna [QuoteValidityDays].
-- Correrlo después de regresar el código a master con la Fase 29B: la guarda no bloquea las
-- tablas, y esta versión podría guardar una cotización entre la guarda y el borrado. Volver al
-- código anterior funciona también sin este script.
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
    WHERE [MigrationId] = N'20261101055834_AddQuotes'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [Quotes])
        OR EXISTS (SELECT 1 FROM [TenantSettings] WHERE [QuoteValidityDays] <> 15)
        THROW 50051, N''Businesses already have quotes, or picked how long they are valid; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101055834_AddQuotes'
)
BEGIN
    DROP TABLE [QuoteLines];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101055834_AddQuotes'
)
BEGIN
    DROP TABLE [Quotes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101055834_AddQuotes'
)
BEGIN
    ALTER TABLE [TenantSettings] DROP CONSTRAINT [CK_TenantSettings_QuoteValidityDays];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101055834_AddQuotes'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'QuoteValidityDays');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [QuoteValidityDays];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101055834_AddQuotes'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101055834_AddQuotes';
END;

COMMIT;
GO
