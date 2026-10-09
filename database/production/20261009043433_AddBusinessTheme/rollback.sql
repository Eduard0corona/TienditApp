-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 16, 20261009043433_AddBusinessTheme,
-- hasta 20261008074715_StopMappingRequireOpenCashSession.
-- Generado el 2026-10-09 con `dotnet ef migrations script
-- 20261009043433_AddBusinessTheme 20261008074715_StopMappingRequireOpenCashSession
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra [TenantSettings].[Theme]: se pierde el tema que eligió cada negocio (la bitácora conserva
-- cada cambio, BusinessThemeUpdated, y el respaldo de la base todo). Nada más cambia: al aplicar de
-- nuevo, todos vuelven a Nopal hasta que su dueño elija otra vez.
-- Solo hace falta para volver a la Fase 15 con la base como estaba; volver al código de la Fase 15
-- funciona también sin este script (no mapea la columna). Para volver a la Fase 14, después corre
-- el rollback.sql de database/production/20261008074715_StopMappingRequireOpenCashSession.
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
