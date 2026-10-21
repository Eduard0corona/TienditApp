-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 27B, 20261021040954_AddInventoryTransfers,
-- hasta la migración anterior, 20261021002749_AddInventoryCounts.
-- Generado el 2026-10-21 con `dotnet ef migrations script
-- 20261021040954_AddInventoryTransfers 20261021002749_AddInventoryCounts
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50031; con -b, sqlcmd termina ahí) si ya hay transferencias:
-- se borraría qué salida y qué entrada van juntas, quién movió los artículos y por qué, y cada
-- pierna quedaría como un movimiento suelto. Entonces solo queda corregir hacia adelante (la
-- consulta 3 de verify.sql las cuenta).
-- Sin ellas, borra la tabla [InventoryTransfers], vacía, con sus restricciones, llaves foráneas e
-- índices.
-- Volver al código anterior (master con la Fase 27A) funciona también sin este script: no conoce
-- la tabla, y las piernas de una transferencia son movimientos de inventario que sabe leer.
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
    WHERE [MigrationId] = N'20261021040954_AddInventoryTransfers'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [InventoryTransfers])
        THROW 50031, N''Businesses made transfers between locations already; rolling back would erase which exit and which entry belong together, who moved the items and why. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021040954_AddInventoryTransfers'
)
BEGIN
    DROP TABLE [InventoryTransfers];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021040954_AddInventoryTransfers'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021040954_AddInventoryTransfers';
END;

COMMIT;
GO
