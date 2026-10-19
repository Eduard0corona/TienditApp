-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24H, 20261018141704_AddBusinessReviews,
-- hasta la migración anterior, 20261018114538_AddBranchLocation.
-- Generado el 2026-10-18 con `dotnet ef migrations script
-- 20261018141704_AddBusinessReviews 20261018114538_AddBranchLocation
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50023; con -b, sqlcmd termina ahí) si algún cliente escribió
-- una reseña: se perderían, con las respuestas de los negocios. Entonces solo queda corregir
-- hacia adelante (la consulta 3 de verify.sql las cuenta). Sin reseñas, borra [BusinessReviews]
-- (vacía). Nada más cambia.
-- Volver al código anterior (master con la Fase 24G) funciona también sin este script: no conoce
-- la tabla.
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
    WHERE [MigrationId] = N'20261018141704_AddBusinessReviews'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [BusinessReviews])
        THROW 50023, N'Customers reviewed businesses; rolling back would delete their reviews. Roll forward instead.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018141704_AddBusinessReviews'
)
BEGIN
    DROP TABLE [BusinessReviews];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018141704_AddBusinessReviews'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018141704_AddBusinessReviews';
END;

COMMIT;
GO
