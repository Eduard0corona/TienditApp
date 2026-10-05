-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261005003444_AddPaymentTipAndCashReceived.
-- Generado el 2026-10-05 con `dotnet ef migrations script
-- 20261005003444_AddPaymentTipAndCashReceived 20261005003434_AddFolioSequences
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Quita OrderPayments.[Tip] y [CashReceived]. Si hay pagos con propina, el script se detiene
-- sin cambiar nada (error 50006): la propina es parte del historial de la venta (su movimiento
-- de caja se conserva). El efectivo recibido solo explicaba el cambio y se puede perder.
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
    WHERE [MigrationId] = N'20261005003444_AddPaymentTipAndCashReceived'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [OrderPayments] WHERE [Tip] <> 0)
        THROW 50006, N''There are payments with a tip; rolling back would lose the tip of each payment.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003444_AddPaymentTipAndCashReceived'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderPayments]') AND [c].[name] = N'CashReceived');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OrderPayments] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OrderPayments] DROP COLUMN [CashReceived];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003444_AddPaymentTipAndCashReceived'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderPayments]') AND [c].[name] = N'Tip');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OrderPayments] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OrderPayments] DROP COLUMN [Tip];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003444_AddPaymentTipAndCashReceived'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003444_AddPaymentTipAndCashReceived';
END;

COMMIT;
GO
