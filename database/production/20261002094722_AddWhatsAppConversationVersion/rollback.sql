-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261002094722_AddWhatsAppConversationVersion.
-- Generado el 2026-10-02 con `dotnet ef migrations script
-- 20261002094722_AddWhatsAppConversationVersion 20261001155915_AddWhatsAppWebhookUniqueIndexes
-- --idempotent` (EF Core 9.0.0). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Quita la columna [Version] de [WhatsAppConversations] y su registro en
-- [__EFMigrationsHistory]. No toca datos de negocio. Idempotente: no hace nada si la
-- migración no está aplicada.
--
-- Ejecútalo solo después de volver a desplegar el código anterior a la PR #20: ese código
-- no usa la columna, pero el de la PR #20 falla sin ella.
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
    WHERE [MigrationId] = N'20261002094722_AddWhatsAppConversationVersion'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WhatsAppConversations]') AND [c].[name] = N'Version');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [WhatsAppConversations] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [WhatsAppConversations] DROP COLUMN [Version];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002094722_AddWhatsAppConversationVersion'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002094722_AddWhatsAppConversationVersion';
END;

COMMIT;
GO
