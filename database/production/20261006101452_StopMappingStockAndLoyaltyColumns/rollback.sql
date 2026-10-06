-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 12, 20261006101452_StopMappingStockAndLoyaltyColumns,
-- hasta 20261006054141_DropPlainRefreshTokenColumns.
-- Generado el 2026-10-06 con `dotnet ef migrations script
-- 20261006101452_StopMappingStockAndLoyaltyColumns 20261006054141_DropPlainRefreshTokenColumns
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Solo quita la migración de [__EFMigrationsHistory]: no cambió el esquema, y las columnas siguen
-- ahí con sus valores. Volver a la Fase 11 no exige este script, porque las lee tal como están.
-- Úsalo solo si además la base debe quedar sin la migración registrada, después de volver a
-- desplegar el código anterior (la versión nueva se marca como no sana mientras le falte su
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
    WHERE [MigrationId] = N'20261006101452_StopMappingStockAndLoyaltyColumns'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006101452_StopMappingStockAndLoyaltyColumns';
END;

COMMIT;
GO
