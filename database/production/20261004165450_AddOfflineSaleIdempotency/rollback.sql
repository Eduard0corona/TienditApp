-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004165450_AddOfflineSaleIdempotency.
-- Generado el 2026-10-04 con `dotnet ef migrations script
-- 20261004165450_AddOfflineSaleIdempotency 20261004163845_AddPlansAndSubscriptions
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Quita Orders.[ClientRequestId], [CapturedOfflineAt] y su índice. Si hay ventas capturadas sin
-- conexión, el script se detiene sin cambiar nada (error 50005): la hora de captura es parte de
-- su historial. Las claves de las demás ventas solo sirven para reintentos y se pueden perder.
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
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [Orders] WHERE [CapturedOfflineAt] IS NOT NULL)
        THROW 50005, N'There are sales captured without connection; rolling back would lose when they were captured.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency'
)
BEGIN
    DROP INDEX [IX_Orders_TenantId_ClientRequestId] ON [Orders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'CapturedOfflineAt');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Orders] DROP COLUMN [CapturedOfflineAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'ClientRequestId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Orders] DROP COLUMN [ClientRequestId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency';
END;

COMMIT;
GO
