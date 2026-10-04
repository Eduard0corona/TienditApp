-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004121539_AddOutboxAndWebhookInbox.
-- Generado el 2026-10-04 con `dotnet ef migrations script
-- 20261004121539_AddOutboxAndWebhookInbox 20261004105827_AddTaxPolicyAndCounterSaleClosing
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra OutboxMessages, OutboxMessageConsumers e InboxMessages. Se pierden los eventos y las
-- entregas del webhook que no se hayan procesado (auditoría y avisos pendientes): antes de
-- revertir, la consulta 3 de verify.sql debe dar 0 pendientes.
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
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    DROP TABLE [InboxMessages];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    DROP TABLE [OutboxMessageConsumers];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    DROP TABLE [OutboxMessages];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox';
END;

COMMIT;
GO
