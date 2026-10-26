-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28F, 20261026082041_AddSelfInvoicing,
-- hasta la migración anterior, 20261026032721_AddCfdiEmails.
-- Generado el 2026-10-26 con `dotnet ef migrations script
-- 20261026082041_AddSelfInvoicing 20261026032721_AddCfdiEmails --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50042; con -b, sqlcmd termina ahí) si algún negocio ya tiene la
-- llave de sus códigos de autofactura, aunque la haya apagado: sin ella, los tickets que ya imprimió
-- dejarían de servir aunque después se corrija hacia adelante. Entonces solo queda corregir hacia
-- adelante (la consulta 3 de verify.sql los cuenta). Sin llaves, borra
-- [CK_InvoicingProfiles_SelfInvoicing], [InvoicingProfiles].[SelfInvoiceKey] y
-- [InvoicingProfiles].[SelfInvoicing]. Las facturas que pidieron los clientes se quedan. Volver al
-- código anterior (master con la Fase 28E) funciona también sin este script.
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
    WHERE [MigrationId] = N'20261026082041_AddSelfInvoicing'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [InvoicingProfiles] WHERE [SelfInvoiceKey] IS NOT NULL)
        THROW 50042, N''A business has the key of its self-invoicing codes already; rolling back would lose it. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026082041_AddSelfInvoicing'
)
BEGIN
    ALTER TABLE [InvoicingProfiles] DROP CONSTRAINT [CK_InvoicingProfiles_SelfInvoicing];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026082041_AddSelfInvoicing'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoicingProfiles]') AND [c].[name] = N'SelfInvoiceKey');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [InvoicingProfiles] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [InvoicingProfiles] DROP COLUMN [SelfInvoiceKey];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026082041_AddSelfInvoicing'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoicingProfiles]') AND [c].[name] = N'SelfInvoicing');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [InvoicingProfiles] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [InvoicingProfiles] DROP COLUMN [SelfInvoicing];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026082041_AddSelfInvoicing'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026082041_AddSelfInvoicing';
END;

COMMIT;
GO
