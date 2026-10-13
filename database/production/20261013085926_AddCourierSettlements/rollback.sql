-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22D, 20261013085926_AddCourierSettlements,
-- hasta la migración anterior, 20261013042014_AddOrderDeliveries.
-- Generado el 2026-10-13 con `dotnet ef migrations script
-- 20261013085926_AddCourierSettlements 20261013042014_AddOrderDeliveries
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Se detiene sin cambiar nada (error 50014; con -b, sqlcmd termina ahí) si hay alguna liquidación
-- de repartidor, o algún pedido que salió con el repartidor o que dijo con cuánto paga: se perdería
-- lo que cobra el repartidor y lo que entregó, aunque los pagos y movimientos que registró la
-- liquidación siguen en sus pedidos y cajas. Con ellos solo queda corregir hacia adelante (la
-- consulta 4 de verify.sql los cuenta).
-- Sin ellos, borra las tres tablas (vacías), las tres CHECK y las cuatro columnas de
-- [OrderDeliveries] (en NULL). Nada más cambia.
-- Volver al código anterior (master con la Fase 22C) funciona también sin este script: no conoce
-- las tablas ni las columnas.
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
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [CourierSettlements])
        OR EXISTS (SELECT 1 FROM [OrderDeliveries] WHERE [DispatchedAt] IS NOT NULL OR [PaysWith] IS NOT NULL)
        THROW 50014, N''There are orders sent out with the courier or courier settlements; rolling back would lose what the courier collects and what it handed over. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    DROP TABLE [CourierSettlementAdjustments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    DROP TABLE [CourierSettlementOrders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    DROP TABLE [CourierSettlements];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    ALTER TABLE [OrderDeliveries] DROP CONSTRAINT [CK_OrderDeliveries_CashToCollect];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    ALTER TABLE [OrderDeliveries] DROP CONSTRAINT [CK_OrderDeliveries_Dispatch];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    ALTER TABLE [OrderDeliveries] DROP CONSTRAINT [CK_OrderDeliveries_PaysWith];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderDeliveries]') AND [c].[name] = N'CashToCollect');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OrderDeliveries] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OrderDeliveries] DROP COLUMN [CashToCollect];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderDeliveries]') AND [c].[name] = N'DispatchedAt');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OrderDeliveries] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OrderDeliveries] DROP COLUMN [DispatchedAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderDeliveries]') AND [c].[name] = N'DispatchedBy');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [OrderDeliveries] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [OrderDeliveries] DROP COLUMN [DispatchedBy];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderDeliveries]') AND [c].[name] = N'PaysWith');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [OrderDeliveries] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [OrderDeliveries] DROP COLUMN [PaysWith];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements';
END;

COMMIT;
GO
