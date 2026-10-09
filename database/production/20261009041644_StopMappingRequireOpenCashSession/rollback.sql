-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261009041644_StopMappingRequireOpenCashSession,
-- hasta 20261006201255_DropStockAndLoyaltyColumns (Fase 13).
-- Generado el 2026-10-09 con `dotnet ef migrations script
-- 20261009041644_StopMappingRequireOpenCashSession 20261006201255_DropStockAndLoyaltyColumns
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Solo quita la migración de [__EFMigrationsHistory]: no cambió el esquema, y la columna sigue
-- ahí con sus valores. Volver a la versión anterior no exige este script, porque la lee tal como
-- está. Úsalo solo si además la base debe quedar sin la migración registrada, después de volver
-- a desplegar el código anterior (la versión nueva se marca como no sana mientras le falte su
-- migración). Si después aplicas apply.sql otra vez, no cambia nada.
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
    WHERE [MigrationId] = N'20261009041644_StopMappingRequireOpenCashSession'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009041644_StopMappingRequireOpenCashSession';
END;

COMMIT;
GO
