-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 13, 20261006201255_DropStockAndLoyaltyColumns,
-- hasta 20261006101452_StopMappingStockAndLoyaltyColumns.
-- Generado el 2026-10-06 con `dotnet ef migrations script
-- 20261006201255_DropStockAndLoyaltyColumns 20261006101452_StopMappingStockAndLoyaltyColumns
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Crea otra vez [Products].[StockQuantity] y [LowStockThreshold] (int, aceptan nulos) y
-- [CustomerProfiles].[LoyaltyPoints] (bigint, sin nulos, 0 por omisión) y [LoyaltyTier]
-- (nvarchar(50), acepta nulos), vacías: los valores no vuelven, solo están en el respaldo. Solo
-- hace falta para volver a la Fase 11, que las lee y trata el NULL y el 0 como "sin existencia
-- capturada" y "sin puntos": primero este script, después el código de la Fase 11. Volver a la
-- Fase 12 no lo necesita. Si después aplicas apply.sql otra vez, las borra de nuevo.
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
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    ALTER TABLE [Products] ADD [LowStockThreshold] int NULL;
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    ALTER TABLE [Products] ADD [StockQuantity] int NULL;
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    ALTER TABLE [CustomerProfiles] ADD [LoyaltyPoints] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    ALTER TABLE [CustomerProfiles] ADD [LoyaltyTier] nvarchar(50) NULL;
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns';
END;

COMMIT;
GO
