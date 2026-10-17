-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24C, 20261017100104_AddOrderTrackingToken,
-- hasta la migración anterior, 20261017070332_AddAccountAddresses.
-- Generado el 2026-10-17 con `dotnet ef migrations script
-- 20261017100104_AddOrderTrackingToken 20261017070332_AddAccountAddresses
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Borra el índice IX_Orders_TrackingToken, la restricción CK_Orders_TrackingToken y la columna
-- [Orders].[TrackingToken]. No pierde datos del negocio, pero los enlaces de seguimiento que ya
-- tienen los clientes dejan de abrir su pedido para siempre: aplicar otra vez la migración no los
-- recupera (la consulta 3 de verify.sql dice cuántos pedidos tienen enlace). Antes de revertir,
-- considera volver solo al código anterior, que funciona sin este script.
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
    WHERE [MigrationId] = N'20261017100104_AddOrderTrackingToken'
)
BEGIN
    DROP INDEX [IX_Orders_TrackingToken] ON [Orders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017100104_AddOrderTrackingToken'
)
BEGIN
    ALTER TABLE [Orders] DROP CONSTRAINT [CK_Orders_TrackingToken];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017100104_AddOrderTrackingToken'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'TrackingToken');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Orders] DROP COLUMN [TrackingToken];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017100104_AddOrderTrackingToken'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017100104_AddOrderTrackingToken';
END;

COMMIT;
GO

