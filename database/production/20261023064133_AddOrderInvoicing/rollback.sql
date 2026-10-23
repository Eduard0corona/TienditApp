-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28A, 20261023064133_AddOrderInvoicing,
-- hasta la migración anterior, 20261022213253_AddOnlinePaymentReconciliation.
-- Generado el 2026-10-23 con `dotnet ef migrations script
-- 20261023064133_AddOrderInvoicing 20261022213253_AddOnlinePaymentReconciliation --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50037; con -b, sqlcmd termina ahí) si ya hay facturas (pedidas,
-- timbradas, rechazadas o canceladas), un CSD registrado o clientes con datos fiscales: borrarlos
-- perdería facturas que el SAT ya tiene. Entonces solo queda corregir hacia adelante (la consulta 4
-- de verify.sql los cuenta).
-- Sin ellos, borra [InvoicingProfiles], [OrderInvoices], [CK_Customers_FiscalData] y las columnas
-- [Customers].[FiscalData_*]. Volver al código anterior (master con la Fase 24K) funciona también
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
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [OrderInvoices])
        OR EXISTS (SELECT 1 FROM [InvoicingProfiles] WHERE [CertificateNumber] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [Customers] WHERE [FiscalData_Rfc] IS NOT NULL)
        THROW 50037, N''Invoices, a CSD or customers'''' fiscal data are recorded already; rolling back would delete them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN
    DROP TABLE [InvoicingProfiles];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN
    DROP TABLE [OrderInvoices];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN
    ALTER TABLE [Customers] DROP CONSTRAINT [CK_Customers_FiscalData];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'FiscalData_CfdiUse');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Customers] DROP COLUMN [FiscalData_CfdiUse];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'FiscalData_FiscalRegime');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Customers] DROP COLUMN [FiscalData_FiscalRegime];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'FiscalData_Name');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Customers] DROP COLUMN [FiscalData_Name];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'FiscalData_PostalCode');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [Customers] DROP COLUMN [FiscalData_PostalCode];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'FiscalData_Rfc');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [Customers] DROP COLUMN [FiscalData_Rfc];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023064133_AddOrderInvoicing';
END;

COMMIT;
GO
