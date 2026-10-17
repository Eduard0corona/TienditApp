-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24B, 20261017070332_AddAccountAddresses,
-- hasta la migración anterior, 20261016162347_AddBusinessDirectory.
-- Generado el 2026-10-17 con `dotnet ef migrations script
-- 20261017070332_AddAccountAddresses 20261016162347_AddBusinessDirectory
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, con
-- la revisión de abajo antes de borrar.
--
-- Se detiene sin cambiar nada (error 50017; con -b, sqlcmd termina ahí) si alguna cuenta guardó
-- direcciones: se perderían, y son de los clientes, no de un negocio (cada pedido ya copió la
-- suya). Con direcciones guardadas solo queda corregir hacia adelante (la consulta 4 de verify.sql
-- las cuenta). Sin direcciones, borra [AccountAddresses] (vacía) y el índice
-- IX_DeliveryZonePostalCodes_PostalCode. Nada más cambia.
-- Volver al código anterior (master con la Fase 24A) funciona también sin este script: no conoce
-- la tabla ni el índice.
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
    WHERE [MigrationId] = N'20261017070332_AddAccountAddresses'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [AccountAddresses])
        THROW 50017, N'Customer accounts saved addresses; rolling back would delete them. Roll forward instead.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017070332_AddAccountAddresses'
)
BEGIN
    DROP TABLE [AccountAddresses];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017070332_AddAccountAddresses'
)
BEGIN
    DROP INDEX [IX_DeliveryZonePostalCodes_PostalCode] ON [DeliveryZonePostalCodes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017070332_AddAccountAddresses'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017070332_AddAccountAddresses';
END;

COMMIT;
GO

