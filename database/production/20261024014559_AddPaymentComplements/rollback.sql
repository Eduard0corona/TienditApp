-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28C, 20261024014559_AddPaymentComplements,
-- hasta la migración anterior, 20261023175930_AddGlobalInvoices.
-- Generado el 2026-10-24 con `dotnet ef migrations script
-- 20261024014559_AddPaymentComplements 20261023175930_AddGlobalInvoices --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50039; con -b, sqlcmd termina ahí) si ya hay complementos de
-- pago o facturas en parcialidades (PPD) (pedidas, timbradas, rechazadas o canceladas): borrarlos
-- perdería CFDI que el SAT ya tiene, y la restricción anterior (solo PUE) rechazaría las facturas
-- PPD. Entonces solo queda corregir hacia adelante (las consultas 3 y 4 de verify.sql los cuentan).
-- Sin ellos, borra [PaymentComplements] y [InvoicingProfiles].[PaymentSerie] y deja otra vez la
-- restricción [CK_OrderInvoices_PaymentMethod] en solo PUE. Volver al código anterior (master con la
-- Fase 28B) funciona también sin este script.
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
    WHERE [MigrationId] = N'20261024014559_AddPaymentComplements'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [PaymentComplements]) OR EXISTS (SELECT 1 FROM [OrderInvoices] WHERE [PaymentMethod] = ''PPD'')
        THROW 50039, N''Payment complements or invoices in installments are recorded already; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024014559_AddPaymentComplements'
)
BEGIN
    DROP TABLE [PaymentComplements];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024014559_AddPaymentComplements'
)
BEGIN
    ALTER TABLE [OrderInvoices] DROP CONSTRAINT [CK_OrderInvoices_PaymentMethod];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024014559_AddPaymentComplements'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoicingProfiles]') AND [c].[name] = N'PaymentSerie');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [InvoicingProfiles] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [InvoicingProfiles] DROP COLUMN [PaymentSerie];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024014559_AddPaymentComplements'
)
BEGIN
    EXEC(N'ALTER TABLE [OrderInvoices] ADD CONSTRAINT [CK_OrderInvoices_PaymentMethod] CHECK ([PaymentMethod] = ''PUE'')');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024014559_AddPaymentComplements'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024014559_AddPaymentComplements';
END;

COMMIT;
GO

