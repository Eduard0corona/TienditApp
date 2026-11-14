-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 31N, 20261113033616_AddQuoteDeposits, hasta la
-- migración anterior, 20261112165041_AddWhatsAppMessageMedia.
-- Generado el 2026-11-13 con `dotnet ef migrations script
-- 20261113033616_AddQuoteDeposits 20261112165041_AddWhatsAppMessageMedia --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- En su transacción: borra la llave, el índice, las restricciones y las tres columnas de la 31N, y
-- deja [CK_OnlineCheckouts_Outcome] como estaba. Antes se detiene con el error 50062, sin cambiar
-- nada, si un negocio pide anticipo, una versión enviada lo pide o un cliente pagó (o fue a pagar)
-- uno: se perdería. La consulta 2 de verify.sql los cuenta. Un anticipo pagado (aplicado, o pagado y
-- devuelto) es dinero que se movió: con uno así, no reviertas; la consulta 2 dice qué hacer en cada caso.
-- Córrelo después de regresar el código a master con la Fase 31M; volver a ese código funciona
-- también sin este script. Toma los mismos candados de esquema que apply.sql. Con -t 15, si tarda
-- más, sqlcmd lo cancela sin cambiar nada.
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
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [OnlineCheckouts] WITH (TABLOCKX, HOLDLOCK) WHERE [QuoteId] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [Quotes] WITH (TABLOCKX, HOLDLOCK) WHERE [DepositAmount] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [TenantSettings] WITH (TABLOCKX, HOLDLOCK) WHERE [QuoteDepositPercent] IS NOT NULL)
        THROW 50062, N''A business asks a deposit to accept its quotes, a quote asks one or a customer paid one; rolling back would lose it (query 2 of verify.sql).'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT [FK_OnlineCheckouts_Quotes_QuoteId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    ALTER TABLE [TenantSettings] DROP CONSTRAINT [CK_TenantSettings_QuoteDepositPercent];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    ALTER TABLE [Quotes] DROP CONSTRAINT [CK_Quotes_Deposit];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    DROP INDEX [IX_OnlineCheckouts_QuoteId] ON [OnlineCheckouts];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT [CK_OnlineCheckouts_Outcome];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT [CK_OnlineCheckouts_Quote];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'QuoteDepositPercent');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [QuoteDepositPercent];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Quotes]') AND [c].[name] = N'DepositAmount');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Quotes] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Quotes] DROP COLUMN [DepositAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OnlineCheckouts]') AND [c].[name] = N'QuoteId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [OnlineCheckouts] DROP COLUMN [QuoteId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    EXEC(N'ALTER TABLE [OnlineCheckouts] ADD CONSTRAINT [CK_OnlineCheckouts_Outcome] CHECK (([Status] = 0 AND [Request] IS NOT NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 1 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 2 AND [Request] IS NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NULL AND [Failure] IS NOT NULL AND [Failure] BETWEEN 1 AND 4))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261113033616_AddQuoteDeposits';
END;

COMMIT;
GO

