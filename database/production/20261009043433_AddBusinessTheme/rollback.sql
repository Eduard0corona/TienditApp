-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 16, 20261009043433_AddBusinessTheme,
-- hasta la migración anterior, 20261009041644_StopMappingRequireOpenCashSession.
-- Generado el 2026-10-09 con `dotnet ef migrations script
-- 20261009043433_AddBusinessTheme 20261009041644_StopMappingRequireOpenCashSession
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra [TenantSettings].[Theme]: se pierde el tema que eligió cada negocio (la bitácora conserva
-- cada cambio, BusinessThemeUpdated, y el respaldo de la base todo). Nada más cambia: al aplicar de
-- nuevo, todos vuelven a Nopal hasta que su dueño elija otra vez.
-- Volver al código anterior (master: la Fase 13 con la PR #47) funciona también sin este script: no
-- mapea la columna. Para dejar la base como en master, después corre el rollback.sql de
-- database/production/20261008074715_StopMappingRequireOpenCashSession, que borra las cancelaciones;
-- la migración de la PR #47 queda registrada.
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
    WHERE [MigrationId] = N'20261009043433_AddBusinessTheme'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'Theme');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [Theme];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009043433_AddBusinessTheme'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009043433_AddBusinessTheme';
END;

COMMIT;
GO
