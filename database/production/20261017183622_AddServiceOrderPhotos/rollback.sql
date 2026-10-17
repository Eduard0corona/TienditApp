-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 25, 20261017183622_AddServiceOrderPhotos,
-- hasta la migración anterior, 20261017100104_AddOrderTrackingToken.
-- Generado el 2026-10-17 con `dotnet ef migrations script
-- 20261017183622_AddServiceOrderPhotos 20261017100104_AddOrderTrackingToken
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Borra la tabla [ServiceOrderPhotos] solo si está vacía. En cuanto un negocio agregó una foto a
-- una orden de servicio, se detiene con THROW 50018 sin cambiar nada: revertir borraría el
-- registro de lo que recibieron y entregaron, y solo queda avanzar (la consulta 3 de verify.sql
-- dice cuántas fotos hay). Antes de revertir, considera volver solo al código anterior, que
-- funciona sin este script.
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
    WHERE [MigrationId] = N'20261017183622_AddServiceOrderPhotos'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [ServiceOrderPhotos])
        THROW 50018, N'Businesses added photos to their service orders; rolling back would delete them. Roll forward instead.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017183622_AddServiceOrderPhotos'
)
BEGIN
    DROP TABLE [ServiceOrderPhotos];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017183622_AddServiceOrderPhotos'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017183622_AddServiceOrderPhotos';
END;

COMMIT;
GO
