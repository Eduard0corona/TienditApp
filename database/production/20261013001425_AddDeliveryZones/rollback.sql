-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22B, 20261013001425_AddDeliveryZones,
-- hasta la migración anterior, 20261012204959_AddBranchServiceMode.
-- Generado el 2026-10-13 con `dotnet ef migrations script
-- 20261013001425_AddDeliveryZones 20261012204959_AddBranchServiceMode
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Se detiene sin cambiar nada (error 50012; con -b, sqlcmd termina ahí) si hay zonas de entrega
-- guardadas, activas o no: se perderían, y la bitácora guarda su nombre, costo y cuántos códigos
-- postales tenía, nunca cuáles. Con zonas guardadas solo queda corregir hacia adelante, o borrarlas
-- a propósito antes en Configuración › Zonas de entrega (la consulta 3 de verify.sql las cuenta).
-- Sin zonas, borra [DeliveryZonePostalCodes] y [DeliveryZones] (vacías). Nada más cambia.
-- Volver al código anterior (master con la Fase 22A) funciona también sin este script: no conoce
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
    WHERE [MigrationId] = N'20261013001425_AddDeliveryZones'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [DeliveryZones])
        THROW 50012, N'There are delivery zones saved; rolling back would delete them. Delete them first, or roll forward.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013001425_AddDeliveryZones'
)
BEGIN
    DROP TABLE [DeliveryZonePostalCodes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013001425_AddDeliveryZones'
)
BEGIN
    DROP TABLE [DeliveryZones];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013001425_AddDeliveryZones'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013001425_AddDeliveryZones';
END;

COMMIT;
GO
