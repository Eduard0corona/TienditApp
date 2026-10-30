-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28M, 20261030181829_AddCfdiWhatsApp,
-- hasta la migración anterior, 20261029163102_AddGlobalInvoiceWriteOffNotes.
-- Generado el 2026-10-30 con `dotnet ef migrations script
-- 20261030181829_AddCfdiWhatsApp 20261029163102_AddGlobalInvoiceWriteOffNotes --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50046; con -b, sqlcmd termina ahí) si ya hay mensajes de CFDI
-- en la cola, o un WhatsApp escrito en una factura, o una plantilla elegida para las facturas:
-- borrarlos perdería ese historial, sus enlaces y lo que el negocio eligió. Entonces solo queda
-- corregir hacia adelante (la consulta 3 de verify.sql los cuenta). Sin ellos, borra las columnas,
-- restricciones e índices nuevos de [OutgoingMessages], [OrderInvoices].[ReceiverWhatsApp] e
-- [InvoicingProfiles].[WhatsAppTemplateId]. Volver al código anterior (master con la Fase 28L)
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
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [OutgoingMessages] WHERE [Purpose] = 5 OR [CfdiId] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [OrderInvoices] WHERE [ReceiverWhatsApp] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [InvoicingProfiles] WHERE [WhatsAppTemplateId] IS NOT NULL)
        THROW 50046, N''Invoice WhatsApp messages are recorded already; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    ALTER TABLE [OutgoingMessages] DROP CONSTRAINT [FK_OutgoingMessages_OrderInvoices_OrderInvoiceId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DROP INDEX [IX_OutgoingMessages_LinkToken] ON [OutgoingMessages];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DROP INDEX [IX_OutgoingMessages_OrderInvoiceId] ON [OutgoingMessages];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DROP INDEX [IX_OutgoingMessages_TenantId_CfdiId_CreatedAt] ON [OutgoingMessages];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    ALTER TABLE [OutgoingMessages] DROP CONSTRAINT [CK_OutgoingMessages_Cfdi];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    ALTER TABLE [OutgoingMessages] DROP CONSTRAINT [CK_OutgoingMessages_CfdiDocument];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    ALTER TABLE [OutgoingMessages] DROP CONSTRAINT [CK_OutgoingMessages_CfdiInvoice];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    ALTER TABLE [OutgoingMessages] DROP CONSTRAINT [CK_OutgoingMessages_LinkToken];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OutgoingMessages]') AND [c].[name] = N'CfdiDocument');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OutgoingMessages] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OutgoingMessages] DROP COLUMN [CfdiDocument];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OutgoingMessages]') AND [c].[name] = N'CfdiId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OutgoingMessages] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OutgoingMessages] DROP COLUMN [CfdiId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OutgoingMessages]') AND [c].[name] = N'LinkToken');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [OutgoingMessages] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [OutgoingMessages] DROP COLUMN [LinkToken];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OutgoingMessages]') AND [c].[name] = N'OrderInvoiceId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [OutgoingMessages] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [OutgoingMessages] DROP COLUMN [OrderInvoiceId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OutgoingMessages]') AND [c].[name] = N'RequestedByUserId');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [OutgoingMessages] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [OutgoingMessages] DROP COLUMN [RequestedByUserId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderInvoices]') AND [c].[name] = N'ReceiverWhatsApp');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [OrderInvoices] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [OrderInvoices] DROP COLUMN [ReceiverWhatsApp];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoicingProfiles]') AND [c].[name] = N'WhatsAppTemplateId');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [InvoicingProfiles] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [InvoicingProfiles] DROP COLUMN [WhatsAppTemplateId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030181829_AddCfdiWhatsApp';
END;

COMMIT;
GO
