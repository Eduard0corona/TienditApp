-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28I, 20261028013618_AddGlobalInvoiceCreditNotes,
-- hasta la migración anterior, 20261026201123_AddIssuedCfdiDateIndexes.
-- Generado el 2026-10-28 con `dotnet ef migrations script
-- 20261028013618_AddGlobalInvoiceCreditNotes 20261026201123_AddIssuedCfdiDateIndexes --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50044; con -b, sqlcmd termina ahí) si existe alguna nota de
-- crédito de una factura global, aunque el PAC la haya rechazado o el SAT ya la haya cancelado:
-- quitar la columna la dejaría sin la factura que acredita. Entonces solo queda corregir hacia
-- adelante (la consulta 3 de verify.sql las cuenta). Sin ellas, borra
-- [FK_CreditNotes_GlobalInvoices_GlobalInvoiceId], [IX_CreditNotes_GlobalInvoiceId],
-- [CK_CreditNotes_Invoice] y [CreditNotes].[GlobalInvoiceId], y [CreditNotes].[OrderInvoiceId]
-- vuelve a ser obligatoria (borra y crea otra vez [IX_CreditNotes_OrderInvoiceId]). Las notas de
-- las facturas de los pedidos se quedan como estaban.
-- Volver al código anterior (master con la Fase 28H) también corre sin este script mientras no
-- exista ninguna nota de una factura global.
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
    WHERE [MigrationId] = N'20261028013618_AddGlobalInvoiceCreditNotes'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [CreditNotes] WHERE [GlobalInvoiceId] IS NOT NULL)
        THROW 50044, N''A credit note of a global invoice exists already; rolling back would lose it. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028013618_AddGlobalInvoiceCreditNotes'
)
BEGIN
    ALTER TABLE [CreditNotes] DROP CONSTRAINT [FK_CreditNotes_GlobalInvoices_GlobalInvoiceId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028013618_AddGlobalInvoiceCreditNotes'
)
BEGIN
    DROP INDEX [IX_CreditNotes_GlobalInvoiceId] ON [CreditNotes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028013618_AddGlobalInvoiceCreditNotes'
)
BEGIN
    ALTER TABLE [CreditNotes] DROP CONSTRAINT [CK_CreditNotes_Invoice];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028013618_AddGlobalInvoiceCreditNotes'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CreditNotes]') AND [c].[name] = N'GlobalInvoiceId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [CreditNotes] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [CreditNotes] DROP COLUMN [GlobalInvoiceId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028013618_AddGlobalInvoiceCreditNotes'
)
BEGIN
    DROP INDEX [IX_CreditNotes_OrderInvoiceId] ON [CreditNotes];
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CreditNotes]') AND [c].[name] = N'OrderInvoiceId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [CreditNotes] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [CreditNotes] ALTER COLUMN [OrderInvoiceId] uniqueidentifier NOT NULL;
    CREATE INDEX [IX_CreditNotes_OrderInvoiceId] ON [CreditNotes] ([OrderInvoiceId]);
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028013618_AddGlobalInvoiceCreditNotes'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028013618_AddGlobalInvoiceCreditNotes';
END;

COMMIT;
GO
