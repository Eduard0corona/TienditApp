-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22G, 20261012145908_AddPlatformSettlements,
-- hasta la migración anterior, 20261012112823_AddPlatformPrices.
-- Generado el 2026-10-12 con `dotnet ef migrations script
-- 20261012145908_AddPlatformSettlements 20261012112823_AddPlatformPrices
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Se detiene sin cambiar nada (error 50010; con -b, sqlcmd termina ahí) si hay liquidaciones
-- guardadas, abiertas, cerradas o anuladas: se perderían y ninguna bitácora conserva sus pedidos ni
-- sus ajustes. Con liquidaciones guardadas solo queda corregir hacia adelante, o exportarlas y
-- borrarlas a propósito antes (la consulta 4 de verify.sql las cuenta). Los pedidos no cambian en
-- ningún caso.
-- Sin liquidaciones, borra [PlatformSettlementAdjustments], [PlatformSettlementOrders] y
-- [PlatformSettlements] (vacías). Nada más cambia.
-- Volver al código anterior (master con la Fase 22F-2) funciona también sin este script: no conoce
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
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [PlatformSettlements])
        THROW 50010, N'There are settlements of delivery platforms saved; rolling back would delete them. Export and delete them first, or roll forward.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    DROP TABLE [PlatformSettlementAdjustments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    DROP TABLE [PlatformSettlementOrders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    DROP TABLE [PlatformSettlements];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements';
END;

COMMIT;
GO
