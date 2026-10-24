-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28B, 20261023175930_AddGlobalInvoices,
-- hasta la migración anterior, 20261023064133_AddOrderInvoicing.
-- Generado el 2026-10-23 con `dotnet ef migrations script
-- 20261023175930_AddGlobalInvoices 20261023064133_AddOrderInvoicing --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50038; con -b, sqlcmd termina ahí) si ya hay facturas globales
-- (pedidas, timbradas, rechazadas o canceladas): borrarlas perdería facturas que el SAT ya tiene y
-- dejaría sus ventas como si nadie las hubiera facturado. Entonces solo queda corregir hacia
-- adelante (la consulta 3 de verify.sql las cuenta).
-- Sin ellas, borra [GlobalInvoiceOrders] y [GlobalInvoices]. Volver al código anterior (master con la
-- Fase 28A) funciona también sin este script.
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
    WHERE [MigrationId] = N'20261023175930_AddGlobalInvoices'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [GlobalInvoices])
        THROW 50038, N''Global invoices are recorded already; rolling back would delete them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023175930_AddGlobalInvoices'
)
BEGIN
    DROP TABLE [GlobalInvoiceOrders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023175930_AddGlobalInvoices'
)
BEGIN
    DROP TABLE [GlobalInvoices];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023175930_AddGlobalInvoices'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023175930_AddGlobalInvoices';
END;

COMMIT;
GO
