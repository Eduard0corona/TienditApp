-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24J, 20261019150436_AddOnlineBookingStaff,
-- hasta la migración anterior, 20261019015524_AddOnlinePayments.
-- Generado el 2026-10-19 con `dotnet ef migrations script
-- 20261019150436_AddOnlineBookingStaff 20261019015524_AddOnlinePayments
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50025; con -b, sqlcmd termina ahí) si algún negocio ya eligió
-- quién atiende sus citas en línea: se perdería quiénes son, sus servicios y su horario. Entonces
-- solo queda corregir hacia adelante (la consulta 3 de verify.sql las cuenta). Sin personas, borra
-- [OnlineBookingStaffServices] y [OnlineBookingStaff] (vacías). Las citas conservan a quién se
-- asignaron. Nada más cambia.
-- Volver al código anterior (master con la Fase 24I) funciona también sin este script: no conoce
-- las tablas.
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
    WHERE [MigrationId] = N'20261019150436_AddOnlineBookingStaff'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [OnlineBookingStaff])
        THROW 50025, N'Businesses set who takes their online appointments; rolling back would delete it. Roll forward instead.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019150436_AddOnlineBookingStaff'
)
BEGIN
    DROP TABLE [OnlineBookingStaffServices];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019150436_AddOnlineBookingStaff'
)
BEGIN
    DROP TABLE [OnlineBookingStaff];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019150436_AddOnlineBookingStaff'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019150436_AddOnlineBookingStaff';
END;

COMMIT;
GO
