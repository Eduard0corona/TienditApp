-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28H, 20261026201123_AddIssuedCfdiDateIndexes,
-- hasta la migración anterior, 20261026145713_AddInvoiceSubstitution.
-- Generado el 2026-10-26 con `dotnet ef migrations script
-- 20261026201123_AddIssuedCfdiDateIndexes 20261026145713_AddInvoiceSubstitution --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra [IX_OrderInvoices_TenantId_IssuedAt], [IX_GlobalInvoices_TenantId_IssuedAt],
-- [IX_PaymentComplements_TenantId_IssuedAt] y [IX_CreditNotes_TenantId_IssuedAt]. No toca datos:
-- solo son índices. Sin ellos, Reportes › CFDI emitidos sigue funcionando, más lento con muchos
-- CFDI. Volver al código anterior (master con la Fase 28G) no lo necesita: los índices no le
-- estorban.
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
    WHERE [MigrationId] = N'20261026201123_AddIssuedCfdiDateIndexes'
)
BEGIN
    DROP INDEX [IX_PaymentComplements_TenantId_IssuedAt] ON [PaymentComplements];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201123_AddIssuedCfdiDateIndexes'
)
BEGIN
    DROP INDEX [IX_OrderInvoices_TenantId_IssuedAt] ON [OrderInvoices];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201123_AddIssuedCfdiDateIndexes'
)
BEGIN
    DROP INDEX [IX_GlobalInvoices_TenantId_IssuedAt] ON [GlobalInvoices];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201123_AddIssuedCfdiDateIndexes'
)
BEGIN
    DROP INDEX [IX_CreditNotes_TenantId_IssuedAt] ON [CreditNotes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201123_AddIssuedCfdiDateIndexes'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201123_AddIssuedCfdiDateIndexes';
END;

COMMIT;
GO
