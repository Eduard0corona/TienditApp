-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28G, 20261026145713_AddInvoiceSubstitution,
-- hasta la migración anterior, 20261026082041_AddSelfInvoicing.
-- Generado el 2026-10-26 con `dotnet ef migrations script
-- 20261026145713_AddInvoiceSubstitution 20261026082041_AddSelfInvoicing --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50043; con -b, sqlcmd termina ahí) si alguna factura ya fue
-- sustituida por otra, aunque el SAT ya la haya cancelado: quitar la columna perdería esa relación y,
-- mientras la sustituida siga viva, el pedido tendría dos facturas vivas que el índice anterior
-- rechaza. Entonces solo queda corregir hacia adelante (la consulta 3 de verify.sql las cuenta). Sin
-- sustituciones, borra [IX_OrderInvoices_ReplacedByInvoiceId], [CK_OrderInvoices_Replacement] y
-- [OrderInvoices].[ReplacedByInvoiceId], y deja
-- [IX_OrderInvoices_TenantId_OrderId_Live] y [CK_OrderInvoices_CancellationMotive] como estaban.
-- Volver al código anterior (master con la Fase 28F) también corre sin este script, pero no sabe de
-- sustituciones: un pedido cuya sustituida sigue viva le muestra dos facturas vivas, y podría
-- relacionar una nota o un complemento con la sustituida o dejar cancelar la nueva antes. Antes de
-- volver, cancela con 02 las sustituidas que sigan vivas (la consulta 3 de verify.sql las cuenta).
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
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [OrderInvoices] WHERE [ReplacedByInvoiceId] IS NOT NULL)
        THROW 50043, N''An invoice has been replaced by another already; rolling back would lose that relation. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution'
)
BEGIN
    DROP INDEX [IX_OrderInvoices_ReplacedByInvoiceId] ON [OrderInvoices];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution'
)
BEGIN
    DROP INDEX [IX_OrderInvoices_TenantId_OrderId_Live] ON [OrderInvoices];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution'
)
BEGIN
    ALTER TABLE [OrderInvoices] DROP CONSTRAINT [CK_OrderInvoices_CancellationMotive];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution'
)
BEGIN
    ALTER TABLE [OrderInvoices] DROP CONSTRAINT [CK_OrderInvoices_Replacement];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInvoices]') AND [c].[name] = N'ReplacedByInvoiceId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OrderInvoices] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OrderInvoices] DROP COLUMN [ReplacedByInvoiceId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrderInvoices_TenantId_OrderId_Live] ON [OrderInvoices] ([TenantId], [OrderId]) WHERE [Status] IN (0, 1, 3)');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution'
)
BEGIN
    EXEC(N'ALTER TABLE [OrderInvoices] ADD CONSTRAINT [CK_OrderInvoices_CancellationMotive] CHECK ([CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 3))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026145713_AddInvoiceSubstitution';
END;

COMMIT;
GO
