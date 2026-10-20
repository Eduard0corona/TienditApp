-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 26A, 20261019202512_AddCustomerCredit,
-- hasta la migración anterior, 20261019150436_AddOnlineBookingStaff.
-- Generado el 2026-10-19 con `dotnet ef migrations script
-- 20261019202512_AddCustomerCredit 20261019150436_AddOnlineBookingStaff
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50026; con -b, sqlcmd termina ahí) si algún cliente tiene
-- límite de fiado o alguna venta se cerró fiada: se perdería a quién le fía cada negocio y qué
-- vendió fiado. Entonces solo queda corregir hacia adelante (la consulta 3 de verify.sql los
-- cuenta). Sin ellos, borra el índice [IX_Orders_TenantId_CustomerPhone] y las columnas
-- [Orders].[SoldOnCredit] y [Customers].[CreditLimit]. Los saldos de los pedidos no cambian.
-- Volver al código anterior (master con la Fase 24J) funciona también sin este script: no conoce
-- las columnas.
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
    WHERE [MigrationId] = N'20261019202512_AddCustomerCredit'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [Customers] WHERE [CreditLimit] IS NOT NULL) OR EXISTS (SELECT 1 FROM [Orders] WHERE [SoldOnCredit] = 1)
        THROW 50026, N''Businesses sell on credit already; rolling back would forget who may owe and what was sold on credit. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019202512_AddCustomerCredit'
)
BEGIN
    DROP INDEX [IX_Orders_TenantId_CustomerPhone] ON [Orders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019202512_AddCustomerCredit'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'SoldOnCredit');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Orders] DROP COLUMN [SoldOnCredit];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019202512_AddCustomerCredit'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'CreditLimit');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Customers] DROP COLUMN [CreditLimit];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019202512_AddCustomerCredit'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019202512_AddCustomerCredit';
END;

COMMIT;
GO
