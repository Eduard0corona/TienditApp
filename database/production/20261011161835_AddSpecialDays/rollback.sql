-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 21, 20261011161835_AddSpecialDays,
-- hasta la migración anterior, 20261010074254_AddOpeningHours.
-- Generado el 2026-10-11 con `dotnet ef migrations script
-- 20261011161835_AddSpecialDays 20261010074254_AddOpeningHours
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra la tabla [SpecialDays]: se pierden los días especiales que marcó cada negocio, también
-- los que ya pasaron (la bitácora conserva cada cambio, SpecialDayAdded, SpecialDayUpdated y
-- SpecialDayRemoved con sus valores, y el respaldo de la base todo; la consulta 4 de verify.sql
-- cuenta lo que se perdería). Nada más cambia: el horario semanal se queda como está, y al aplicar
-- de nuevo la tabla empieza vacía.
-- Volver al código anterior (master con la Fase 20) funciona también sin este script: no conoce
-- la tabla, y los días especiales se quedan guardados sin aplicarse.
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
    WHERE [MigrationId] = N'20261011161835_AddSpecialDays'
)
BEGIN
    DROP TABLE [SpecialDays];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011161835_AddSpecialDays'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011161835_AddSpecialDays';
END;

COMMIT;
GO
