-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28E, 20261026032721_AddCfdiEmails,
-- hasta la migración anterior, 20261024084435_AddCreditNotes.
-- Generado el 2026-10-26 con `dotnet ef migrations script
-- 20261026032721_AddCfdiEmails 20261024084435_AddCreditNotes --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50041; con -b, sqlcmd termina ahí) si ya hay correos de CFDI
-- registrados, o correos escritos en una factura o en un cliente: borrarlos perdería ese historial y
-- lo que el negocio escribió. Entonces solo queda corregir hacia adelante (la consulta 3 de
-- verify.sql los cuenta). Sin ellos, borra [CfdiEmails], [OrderInvoices].[ReceiverEmail] y
-- [Customers].[InvoiceEmail]. Volver al código anterior (master con la Fase 28D) funciona también
-- sin este script.
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
    WHERE [MigrationId] = N'20261026032721_AddCfdiEmails'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [CfdiEmails])
        OR EXISTS (SELECT 1 FROM [OrderInvoices] WHERE [ReceiverEmail] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [Customers] WHERE [InvoiceEmail] IS NOT NULL)
        THROW 50041, N''Invoice emails are recorded already; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026032721_AddCfdiEmails'
)
BEGIN
    DROP TABLE [CfdiEmails];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026032721_AddCfdiEmails'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInvoices]') AND [c].[name] = N'ReceiverEmail');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OrderInvoices] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OrderInvoices] DROP COLUMN [ReceiverEmail];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026032721_AddCfdiEmails'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'InvoiceEmail');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Customers] DROP COLUMN [InvoiceEmail];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026032721_AddCfdiEmails'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026032721_AddCfdiEmails';
END;

COMMIT;
GO

