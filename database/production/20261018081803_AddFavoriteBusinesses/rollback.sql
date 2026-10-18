-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24F, 20261018081803_AddFavoriteBusinesses,
-- hasta la migración anterior, 20261018032200_AddBusinessCover.
-- Generado el 2026-10-18 con `dotnet ef migrations script
-- 20261018081803_AddFavoriteBusinesses 20261018032200_AddBusinessCover
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50021; con -b, sqlcmd termina ahí) si alguna cuenta guardó
-- un negocio favorito: se perderían, y son de los clientes, no de un negocio. Entonces solo queda
-- corregir hacia adelante (la consulta 3 de verify.sql los cuenta). Sin favoritos, borra
-- [FavoriteBusinesses] (vacía). Nada más cambia.
-- Volver al código anterior (master con la Fase 24D-2) funciona también sin este script: no
-- conoce la tabla.
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
    WHERE [MigrationId] = N'20261018081803_AddFavoriteBusinesses'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [FavoriteBusinesses])
        THROW 50021, N'Customer accounts saved favorite businesses; rolling back would delete them. Roll forward instead.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018081803_AddFavoriteBusinesses'
)
BEGIN
    DROP TABLE [FavoriteBusinesses];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018081803_AddFavoriteBusinesses'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018081803_AddFavoriteBusinesses';
END;

COMMIT;
GO

