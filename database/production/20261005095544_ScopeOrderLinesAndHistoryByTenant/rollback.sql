-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 8, 20261005095544_ScopeOrderLinesAndHistoryByTenant
-- y 20261005092153_RevocableSessions, hasta 20261005045624_AddPurchaseOrderClosing.
-- Generado el 2026-10-05 con `dotnet ef migrations script
-- 20261005095544_ScopeOrderLinesAndHistoryByTenant 20261005045624_AddPurchaseOrderClosing
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Quita OrderItems.[TenantId] y OrderStatusHistory.[TenantId], que se derivan de su orden (no
-- se pierde nada), y las columnas nuevas de RefreshTokens. Los tokens que la versión nueva ya
-- guardó como hash no pueden volver a texto: su [Token] ya está vacío (''), la columna vuelve a
-- ser obligatoria y quien tenga una de esas sesiones debe iniciar sesión otra vez. Si después
-- vuelves a aplicar apply.sql, esas filas quedan sin hash y no abren ninguna sesión.
-- La versión anterior funciona con las columnas nuevas: volver a desplegarla no exige este
-- script (con ella, también terminan las sesiones guardadas como hash). Úsalo solo si además hay
-- que quitar el esquema, después de volver a desplegar el código anterior.
-- Idempotente: no hace nada si las migraciones no están aplicadas.
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
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    DROP INDEX [IX_OrderStatusHistory_TenantId_OrderId] ON [OrderStatusHistory];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    DROP INDEX [IX_OrderItems_TenantId_OrderId] ON [OrderItems];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderStatusHistory]') AND [c].[name] = N'TenantId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OrderStatusHistory] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OrderStatusHistory] DROP COLUMN [TenantId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderItems]') AND [c].[name] = N'TenantId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OrderItems] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OrderItems] DROP COLUMN [TenantId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant';
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    DROP INDEX [IX_RefreshTokens_SessionId] ON [RefreshTokens];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    DROP INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RefreshTokens]') AND [c].[name] = N'RevocationReason');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [RefreshTokens] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [RefreshTokens] DROP COLUMN [RevocationReason];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RefreshTokens]') AND [c].[name] = N'SessionId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [RefreshTokens] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [RefreshTokens] DROP COLUMN [SessionId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RefreshTokens]') AND [c].[name] = N'TokenHash');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [RefreshTokens] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [RefreshTokens] DROP COLUMN [TokenHash];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    DROP INDEX [IX_RefreshTokens_Token] ON [RefreshTokens];
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RefreshTokens]') AND [c].[name] = N'Token');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [RefreshTokens] DROP CONSTRAINT ' + @var5 + ';');
    EXEC(N'UPDATE [RefreshTokens] SET [Token] = N'''' WHERE [Token] IS NULL');
    ALTER TABLE [RefreshTokens] ALTER COLUMN [Token] nvarchar(500) NOT NULL;
    ALTER TABLE [RefreshTokens] ADD DEFAULT N'' FOR [Token];
    CREATE INDEX [IX_RefreshTokens_Token] ON [RefreshTokens] ([Token]);
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions';
END;

COMMIT;
GO
