-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22F-2, 20261012112823_AddPlatformPrices,
-- hasta la migración anterior, 20261012005540_AddDeliveryPlatformOrders.
-- Generado el 2026-10-12 con `dotnet ef migrations script
-- 20261012112823_AddPlatformPrices 20261012005540_AddDeliveryPlatformOrders
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Se detiene sin cambiar nada (error 50009; con -b, sqlcmd termina ahí) si hay precios por
-- plataforma guardados: se perderían y, a diferencia de la configuración de cada plataforma, ninguna
-- bitácora los conserva. Con precios guardados solo queda corregir hacia adelante, o exportarlos y
-- borrarlos a propósito antes (la consulta 4 de verify.sql los cuenta). Los pedidos ya registrados no
-- cambian en ningún caso: cada línea guarda su precio.
-- Sin precios, borra [ProductOptionPlatformPrices] y [ProductPlatformPrices] (vacías). Nada más
-- cambia.
-- Volver al código anterior (master con la Fase 22F) funciona también sin este script: no conoce
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
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [ProductPlatformPrices])
       OR EXISTS (SELECT 1 FROM [ProductOptionPlatformPrices])
        THROW 50009, N'There are prices of delivery platforms saved; rolling back would delete them. Export and delete them first, or roll forward.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    DROP TABLE [ProductOptionPlatformPrices];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    DROP TABLE [ProductPlatformPrices];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices';
END;

COMMIT;
GO
