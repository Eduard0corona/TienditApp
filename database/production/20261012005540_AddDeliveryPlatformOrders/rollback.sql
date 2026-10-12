-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22F, 20261012005540_AddDeliveryPlatformOrders,
-- hasta la migración anterior, 20261011161835_AddSpecialDays.
-- Generado el 2026-10-12 con `dotnet ef migrations script
-- 20261012005540_AddDeliveryPlatformOrders 20261011161835_AddSpecialDays
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Se detiene sin cambiar nada (error 50008; con -b, sqlcmd termina ahí) si hay un pedido de Rappi,
-- Uber Eats o DiDi Food (canal 3 a 5) o un pago, reembolso o movimiento de caja con el método
-- «Plataforma» (5): se perderían su folio, su comisión y su neto esperado, y el código anterior no
-- conoce ese canal ni ese método. Desde el primer pedido de plataforma no hay rollback de la base,
-- solo roll-forward; la consulta 5 de verify.sql muestra qué lo detiene.
-- Sin ellos, borra [PlatformOrderDetails] (vacía), [DeliveryPlatformSettings] (se pierde la
-- configuración de cada plataforma; la bitácora DeliveryPlatformUpdated conserva cada valor y la
-- consulta 5 de verify.sql cuenta lo que se perdería) y [DailySalesSnapshots].[PlatformAmount] con
-- su valor por omisión. Nada más cambia.
-- Volver al código anterior (master con la Fase 21) funciona también sin este script mientras no
-- haya pedidos de plataforma: no conoce las tablas ni la columna.
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
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [Orders] WHERE [Channel] IN (3, 4, 5))
       OR EXISTS (SELECT 1 FROM [OrderPayments] WHERE [Method] = 5)
       OR EXISTS (SELECT 1 FROM [OrderRefunds] WHERE [Method] = 5)
       OR EXISTS (SELECT 1 FROM [CashTransactions] WHERE [PaymentMethod] = 5)
        THROW 50008, N'There are delivery platform orders or payments; rolling back would lose their folio, commission and expected settlement, and the previous code does not know their channel or payment method.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    DROP TABLE [PlatformOrderDetails];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    DROP TABLE [DeliveryPlatformSettings];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DailySalesSnapshots]') AND [c].[name] = N'PlatformAmount');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [DailySalesSnapshots] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [DailySalesSnapshots] DROP COLUMN [PlatformAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders';
END;

COMMIT;
GO
