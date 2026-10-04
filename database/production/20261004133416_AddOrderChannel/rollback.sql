-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004133416_AddOrderChannel.
-- Generado el 2026-10-04 con `dotnet ef migrations script
-- 20261004133416_AddOrderChannel 20261004131615_AddMessagingCampaignsAndReminders
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Quita Orders.[Channel]. Se pierde qué órdenes se tomaron desde WhatsApp; nada más cambia.
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
    WHERE [MigrationId] = N'20261004133416_AddOrderChannel'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'Channel');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Orders] DROP COLUMN [Channel];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004133416_AddOrderChannel'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004133416_AddOrderChannel';
END;

COMMIT;
GO
