-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 9, 20261005175643_StopUsingPlainRefreshTokenValues,
-- hasta 20261005095544_ScopeOrderLinesAndHistoryByTenant.
-- Generado el 2026-10-05 con `dotnet ef migrations script
-- 20261005175643_StopUsingPlainRefreshTokenValues 20261005095544_ScopeOrderLinesAndHistoryByTenant
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Solo quita la migración de [__EFMigrationsHistory]: no cambió el esquema, y los valores en
-- texto que convirtió a hash no pueden (ni necesitan) volver, porque la Fase 8 funciona con el
-- hash. Volver a la versión anterior no exige este script. Úsalo solo si además la base
-- debe quedar sin la migración registrada, después de volver a desplegar el código anterior. Si
-- después aplicas apply.sql otra vez, no cambia nada.
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
    WHERE [MigrationId] = N'20261005175643_StopUsingPlainRefreshTokenValues'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005175643_StopUsingPlainRefreshTokenValues';
END;

COMMIT;
GO
