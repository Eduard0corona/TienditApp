-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261005003434_AddFolioSequences.
-- Generado el 2026-10-05 con `dotnet ef migrations script
-- 20261005003434_AddFolioSequences 20261004232541_AddChargePermissionAndSmsCooldown
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra FolioSequences. El código anterior numera contando: las órdenes del negocio, y los
-- movimientos y compras del día. Si la numeración tiene huecos, repetiría un folio y la venta o
-- el movimiento fallarían: las ventas, hasta corregir el negocio; los movimientos y compras,
-- hasta que cambie el día (UTC). La consulta 4 de verify.sql muestra los negocios afectados.
-- Las ventas, compras y movimientos manuales devuelven su folio si se rechazan; un cambio de
-- estado que consume insumos y falla todavía puede dejar un hueco en los movimientos.
-- Idempotente: no hace nada si la migración no está aplicada.
--
-- Ejecútalo solo después de volver a desplegar el código anterior, y revierte primero las
-- migraciones posteriores (cada carpeta tiene su rollback.sql), de la más nueva a la más vieja.
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
    WHERE [MigrationId] = N'20261005003434_AddFolioSequences'
)
BEGIN
    DROP TABLE [FolioSequences];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003434_AddFolioSequences'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003434_AddFolioSequences';
END;

COMMIT;
GO
