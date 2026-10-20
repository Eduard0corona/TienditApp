-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 26B, 20261020003342_AddCustomerWriteOffs,
-- hasta la migración anterior, 20261019202512_AddCustomerCredit.
-- Generado el 2026-10-20 con `dotnet ef migrations script
-- 20261020003342_AddCustomerWriteOffs 20261019202512_AddCustomerCredit
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50027; con -b, sqlcmd termina ahí) si algún negocio ya
-- condonó algo o puso plazo al fiado: lo condonado volvería a verse como deuda y se perdería el
-- plazo. Entonces solo queda corregir hacia adelante (la consulta 3 de verify.sql los cuenta).
-- Sin ellos, borra la tabla [OrderWriteOffs], las restricciones [CK_Orders_WrittenOffAmount] y
-- [CK_TenantSettings_CreditTermDays] y las columnas [Orders].[WrittenOffAmount] y
-- [TenantSettings].[CreditTermDays]. Los saldos de los pedidos no cambian.
-- Volver al código anterior (master con la Fase 26A) funciona también sin este script: no conoce
-- la tabla ni las columnas.
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
    WHERE [MigrationId] = N'20261020003342_AddCustomerWriteOffs'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [OrderWriteOffs]) OR EXISTS (SELECT 1 FROM [TenantSettings] WHERE [CreditTermDays] IS NOT NULL)
        THROW 50027, N''Businesses wrote off customer debts or set a credit term already; rolling back would bring forgiven debts back as owed and forget the term. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020003342_AddCustomerWriteOffs'
)
BEGIN
    DROP TABLE [OrderWriteOffs];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020003342_AddCustomerWriteOffs'
)
BEGIN
    ALTER TABLE [TenantSettings] DROP CONSTRAINT [CK_TenantSettings_CreditTermDays];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020003342_AddCustomerWriteOffs'
)
BEGIN
    ALTER TABLE [Orders] DROP CONSTRAINT [CK_Orders_WrittenOffAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020003342_AddCustomerWriteOffs'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'CreditTermDays');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [CreditTermDays];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020003342_AddCustomerWriteOffs'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'WrittenOffAmount');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Orders] DROP COLUMN [WrittenOffAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020003342_AddCustomerWriteOffs'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020003342_AddCustomerWriteOffs';
END;

COMMIT;
GO
