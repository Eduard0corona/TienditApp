-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29G, 20261102151555_AddQuoteOnlineReplies,
-- hasta la migración anterior, 20261102073115_AddAppointmentWorkOrder.
-- Generado el 2026-11-02 con `dotnet ef migrations script
-- 20261102151555_AddQuoteOnlineReplies 20261102073115_AddAppointmentWorkOrder --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50055; con -b, sqlcmd termina ahí) en cuanto un cliente
-- respondió una cotización desde su enlace (no cabe en la restricción anterior, y se perdería por
-- dónde respondió) o un negocio apagó esa respuesta (se perdería, y al volver a aplicar quedaría
-- encendida otra vez). Entonces solo queda corregir hacia adelante (las consultas 3 y 4 de
-- verify.sql los cuentan). Si no, borra la columna y vuelve a crear [CK_Quotes_Reply] como antes.
-- Correrlo después de regresar el código a master con la Fase 29F: la guarda no bloquea la tabla,
-- y esta versión podría apagar la opción entre la guarda y el borrado. Volver al código anterior
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
    WHERE [MigrationId] = N'20261102151555_AddQuoteOnlineReplies'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [Quotes] WHERE [ReplyChannel] = 4)
        THROW 50055, N''Customers already answered quotes from their link; rolling back would not keep how they answered. Roll forward instead.'', 1;
    IF EXISTS (SELECT 1 FROM [TenantSettings] WHERE [QuoteOnlineReplies] = 0)
        THROW 50055, N''A business turned off the answers to its quotes from their link; rolling back would forget it and turn them on again when rolled forward. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102151555_AddQuoteOnlineReplies'
)
BEGIN
    ALTER TABLE [Quotes] DROP CONSTRAINT [CK_Quotes_Reply];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102151555_AddQuoteOnlineReplies'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'QuoteOnlineReplies');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [QuoteOnlineReplies];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102151555_AddQuoteOnlineReplies'
)
BEGIN
    EXEC(N'ALTER TABLE [Quotes] ADD CONSTRAINT [CK_Quotes_Reply] CHECK (([ReplyChannel] IS NULL AND [CustomerResponse] IS NULL AND [Status] NOT IN (2, 3)) OR ([ReplyChannel] IN (0, 1, 2, 3) AND [Status] IN (2, 3, 5)))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102151555_AddQuoteOnlineReplies'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102151555_AddQuoteOnlineReplies';
END;

COMMIT;
GO
