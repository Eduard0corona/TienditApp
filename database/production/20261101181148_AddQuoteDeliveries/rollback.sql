-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29D, 20261101181148_AddQuoteDeliveries,
-- hasta la migración anterior, 20261101055834_AddQuotes.
-- Generado el 2026-11-01 con `dotnet ef migrations script
-- 20261101181148_AddQuoteDeliveries 20261101055834_AddQuotes --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50052; con -b, sqlcmd termina ahí) en cuanto un negocio mandó
-- una cotización desde TienditApp (por correo o por WhatsApp, aunque siga pendiente) o eligió una
-- plantilla de WhatsApp para ellas: se perderían, y un WhatsApp de una cotización rompería la regla
-- anterior de la cola. Entonces solo queda corregir hacia adelante (la consulta 3 de verify.sql los
-- cuenta). Si no, borra la tabla nueva, los índices, la llave y las columnas, y vuelve a crear
-- [CK_OutgoingMessages_Cfdi] como estaba.
-- Correrlo después de regresar el código a master con la Fase 29C: la guarda no bloquea las tablas,
-- y esta versión podría encolar un envío entre la guarda y el borrado. Volver al código anterior
-- funciona también sin este script.
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
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [QuoteEmails])
        OR EXISTS (SELECT 1 FROM [OutgoingMessages] WHERE [QuoteId] IS NOT NULL OR [Purpose] = 6)
        OR EXISTS (SELECT 1 FROM [TenantSettings] WHERE [QuoteWhatsAppTemplateId] IS NOT NULL)
        THROW 50052, N''Businesses already sent quotes from TienditApp, or picked a WhatsApp template for them; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    ALTER TABLE [OutgoingMessages] DROP CONSTRAINT [FK_OutgoingMessages_Quotes_QuoteId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    DROP TABLE [QuoteEmails];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    DROP INDEX [IX_OutgoingMessages_QuoteId_CreatedAt] ON [OutgoingMessages];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    DROP INDEX [IX_OutgoingMessages_Quotes_TenantId_CreatedAt] ON [OutgoingMessages];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    ALTER TABLE [OutgoingMessages] DROP CONSTRAINT [CK_OutgoingMessages_Cfdi];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    ALTER TABLE [OutgoingMessages] DROP CONSTRAINT [CK_OutgoingMessages_Quote];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'QuoteWhatsAppTemplateId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [QuoteWhatsAppTemplateId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OutgoingMessages]') AND [c].[name] = N'QuoteId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OutgoingMessages] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OutgoingMessages] DROP COLUMN [QuoteId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    EXEC(N'ALTER TABLE [OutgoingMessages] ADD CONSTRAINT [CK_OutgoingMessages_Cfdi] CHECK (([Purpose] = 5 AND [CfdiDocument] IS NOT NULL AND [CfdiId] IS NOT NULL AND [OrderInvoiceId] IS NOT NULL AND [LinkToken] IS NOT NULL) OR ([Purpose] <> 5 AND [CfdiDocument] IS NULL AND [CfdiId] IS NULL AND [OrderInvoiceId] IS NULL AND [RequestedByUserId] IS NULL AND [LinkToken] IS NULL))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101181148_AddQuoteDeliveries';
END;

COMMIT;
GO
