-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 27A, 20261021002749_AddInventoryCounts,
-- hasta la migración anterior, 20261020145628_AddCustomerBalance.
-- Generado el 2026-10-21 con `dotnet ef migrations script
-- 20261021002749_AddInventoryCounts 20261020145628_AddCustomerBalance
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50030; con -b, sqlcmd termina ahí) si ya hay conteos, abiertos,
-- aplicados o cancelados: se borraría lo que se contó, quién y contra qué, y los ajustes de los
-- conteos aplicados se quedarían sin el conteo que los explica. Entonces solo queda corregir hacia
-- adelante (la consulta 3 de verify.sql los cuenta).
-- Sin ellos, borra las tablas [InventoryCountLines] e [InventoryCounts], vacías, con sus
-- restricciones, llaves foráneas e índices.
-- Volver al código anterior (master con la Fase 26D) funciona también sin este script: no conoce
-- las tablas, y los ajustes de un conteo son movimientos de inventario que sabe leer.
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
    WHERE [MigrationId] = N'20261021002749_AddInventoryCounts'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [InventoryCounts])
        THROW 50030, N''Businesses started physical counts already; rolling back would erase what was counted, by whom and against what, and leave their adjustments without the count that explains them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021002749_AddInventoryCounts'
)
BEGIN
    DROP TABLE [InventoryCountLines];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021002749_AddInventoryCounts'
)
BEGIN
    DROP TABLE [InventoryCounts];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021002749_AddInventoryCounts'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021002749_AddInventoryCounts';
END;

COMMIT;
GO
