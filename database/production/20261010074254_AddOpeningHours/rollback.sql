-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 19, 20261010074254_AddOpeningHours,
-- hasta la migración anterior, 20261009071957_DropRequireOpenCashSession.
-- Generado el 2026-10-10 con `dotnet ef migrations script
-- 20261010074254_AddOpeningHours 20261009071957_DropRequireOpenCashSession
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra [TenantSettings].[OpeningHours]: se pierde el horario que capturó cada negocio (la
-- bitácora conserva cada cambio, OpeningHoursUpdated, y el respaldo de la base todo). Nada más
-- cambia: al aplicar de nuevo, ningún negocio tiene horario hasta que su dueño lo capture otra vez.
-- Volver al código anterior (master con la Fase 18) funciona también sin este script: no mapea la
-- columna.
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
    WHERE [MigrationId] = N'20261010074254_AddOpeningHours'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'OpeningHours');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [OpeningHours];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010074254_AddOpeningHours'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010074254_AddOpeningHours';
END;

COMMIT;
GO
