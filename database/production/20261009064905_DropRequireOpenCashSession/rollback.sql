-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261009064905_DropRequireOpenCashSession,
-- hasta 20261009041644_StopMappingRequireOpenCashSession.
-- Generado el 2026-10-09 con `dotnet ef migrations script
-- 20261009064905_DropRequireOpenCashSession 20261009041644_StopMappingRequireOpenCashSession
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Crea otra vez [TenantSettings].[RequireOpenCashSession] (bit, sin nulos, 0 por omisión): los
-- valores no vuelven, solo están en el respaldo, así que todos los negocios quedan con la
-- política desactivada. Solo hace falta para volver a una versión anterior al PR #47, que la lee:
-- primero este script, después ese código. Volver a la del PR #47 no lo necesita. Desde el PR #40
-- ninguna versión usa su valor (todo cobro exige caja); en una anterior, actívala otra vez en los
-- negocios que la tenían, según el respaldo. Si después aplicas apply.sql otra vez, la borra de
-- nuevo.
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
    WHERE [MigrationId] = N'20261009064905_DropRequireOpenCashSession'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [RequireOpenCashSession] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009064905_DropRequireOpenCashSession'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009064905_DropRequireOpenCashSession';
END;

COMMIT;
GO
