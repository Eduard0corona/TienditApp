-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22C, 20261013042014_AddOrderDeliveries,
-- hasta la migración anterior, 20261013002425_AddDeliveryZones.
-- Generado el 2026-10-13 con `dotnet ef migrations script
-- 20261013042014_AddOrderDeliveries 20261013002425_AddDeliveryZones
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Se detiene sin cambiar nada (error 50013; con -b, sqlcmd termina ahí) si hay algún pedido
-- mandado a domicilio, en cualquier estado: perdería su dirección y lo que cobró de envío, y su
-- total, que lo incluye, ya no cuadraría con sus productos. Con pedidos a domicilio solo queda
-- corregir hacia adelante (la consulta 4 de verify.sql los cuenta).
-- Sin ellos, borra [OrderDeliveries] (vacía). Nada más cambia.
-- Volver al código anterior (master con la Fase 22B) funciona también sin este script: no conoce
-- la tabla.
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
    WHERE [MigrationId] = N'20261013042014_AddOrderDeliveries'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [OrderDeliveries])
        THROW 50013, N'There are orders sent to an address; rolling back would lose their address and delivery fee. Roll forward instead.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013042014_AddOrderDeliveries'
)
BEGIN
    DROP TABLE [OrderDeliveries];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013042014_AddOrderDeliveries'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013042014_AddOrderDeliveries';
END;

COMMIT;
GO
