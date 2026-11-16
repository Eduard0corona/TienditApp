-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 32E, 20261116030110_AddAssistantConnections, hasta la
-- migración anterior, 20261115151611_AddAssistantOrders.
-- Generado el 2026-11-16 con `dotnet ef migrations script
-- 20261116030110_AddAssistantConnections 20261115151611_AddAssistantOrders --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- En su transacción: borra las tablas [AssistantAuthorizationCodes], [AssistantTokens],
-- [AssistantConnections] y [AssistantClients]. Antes se detiene con el error 50065, sin cambiar
-- nada, si un cliente tiene un asistente conectado a su cuenta: se desconectaría sin aviso. La
-- consulta 2 de verify.sql los cuenta y dice qué hacer.
-- Córrelo después de regresar el código a master con la Fase 32D; volver a ese código funciona
-- también sin este script. Con -t 15, si tarda más, sqlcmd lo cancela sin cambiar nada.
-- Idempotente: no hace nada si la migración no está aplicada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -t 15 -i rollback.sql
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
    WHERE [MigrationId] = N'20261116030110_AddAssistantConnections'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [AssistantConnections] WITH (TABLOCKX, HOLDLOCK) WHERE [RevokedAt] IS NULL)
        THROW 50065, N''Customers have assistants connected to their accounts; rolling back would disconnect them (query 2 of verify.sql). Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116030110_AddAssistantConnections'
)
BEGIN
    DROP TABLE [AssistantAuthorizationCodes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116030110_AddAssistantConnections'
)
BEGIN
    DROP TABLE [AssistantTokens];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116030110_AddAssistantConnections'
)
BEGIN
    DROP TABLE [AssistantConnections];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116030110_AddAssistantConnections'
)
BEGIN
    DROP TABLE [AssistantClients];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116030110_AddAssistantConnections'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116030110_AddAssistantConnections';
END;

COMMIT;
GO

