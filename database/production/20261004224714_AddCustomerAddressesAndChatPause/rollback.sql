-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004224714_AddCustomerAddressesAndChatPause.
-- Generado el 2026-10-05 con `dotnet ef migrations script
-- 20261004224714_AddCustomerAddressesAndChatPause 20261004221936_AddAppointmentResourcesAndStockPolicy
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra las direcciones de los clientes, sus notas y las pausas de las respuestas automáticas.
-- La consulta 4 de verify.sql cuenta lo que se perdería.
-- Idempotente: no hace nada si la migración no está aplicada.
--
-- Ejecútalo solo después de volver a desplegar el código anterior, y revierte primero las
-- migraciones posteriores (cada carpeta tiene su rollback.sql), de la más nueva a la más vieja.
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
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    DROP TABLE [CustomerAddresses];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WhatsAppConversations]') AND [c].[name] = N'AutoRepliesPausedUntil');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [WhatsAppConversations] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [WhatsAppConversations] DROP COLUMN [AutoRepliesPausedUntil];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'Notes');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Customers] DROP COLUMN [Notes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause';
END;

COMMIT;
GO
