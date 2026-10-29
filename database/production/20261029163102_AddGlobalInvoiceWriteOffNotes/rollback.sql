-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28J, 20261029163102_AddGlobalInvoiceWriteOffNotes,
-- hasta la migración anterior, 20261028013618_AddGlobalInvoiceCreditNotes.
-- Generado el 2026-10-29 con `dotnet ef migrations script
-- 20261029163102_AddGlobalInvoiceWriteOffNotes 20261028013618_AddGlobalInvoiceCreditNotes --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50045; con -b, sqlcmd termina ahí) si existe alguna nota de
-- crédito de una condonación de una venta de una factura global, aunque el PAC la haya rechazado o
-- el SAT ya la haya cancelado: la restricción anterior no la acepta. Entonces solo queda corregir
-- hacia adelante (la consulta 3 de verify.sql las cuenta). Sin ellas, [CK_CreditNotes_Invoice]
-- vuelve a exigir un reembolso en la nota de una factura global. Las demás notas se quedan como
-- estaban.
-- Volver al código anterior (master con la Fase 28I) también corre sin este script.
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
    WHERE [MigrationId] = N'20261029163102_AddGlobalInvoiceWriteOffNotes'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [CreditNotes] WHERE [GlobalInvoiceId] IS NOT NULL AND [OrderWriteOffId] IS NOT NULL)
        THROW 50045, N''A credit note of a write-off of a global invoice exists already; rolling back would lose it. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029163102_AddGlobalInvoiceWriteOffNotes'
)
BEGIN
    ALTER TABLE [CreditNotes] DROP CONSTRAINT [CK_CreditNotes_Invoice];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029163102_AddGlobalInvoiceWriteOffNotes'
)
BEGIN
    EXEC(N'ALTER TABLE [CreditNotes] ADD CONSTRAINT [CK_CreditNotes_Invoice] CHECK (([OrderInvoiceId] IS NOT NULL AND [GlobalInvoiceId] IS NULL) OR ([OrderInvoiceId] IS NULL AND [GlobalInvoiceId] IS NOT NULL AND [OrderRefundId] IS NOT NULL AND [AfterInstallment] = 0))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029163102_AddGlobalInvoiceWriteOffNotes'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029163102_AddGlobalInvoiceWriteOffNotes';
END;

COMMIT;
GO
