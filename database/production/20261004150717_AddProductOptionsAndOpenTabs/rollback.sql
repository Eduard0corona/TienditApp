-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004150717_AddProductOptionsAndOpenTabs.
-- Generado el 2026-10-04 con `dotnet ef migrations script
-- 20261004150717_AddProductOptionsAndOpenTabs 20261004142352_AddDecimalQuantitiesAndCfdiData
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra mesas, rondas a cocina, variantes y extras de los productos y las opciones elegidas en
-- cada venta (el precio de las líneas se conserva; se pierde su descripción), y vuelve
-- [TableNumber] a 10 caracteres. Si algún pedido tiene una mesa de más de 10 caracteres, el
-- script se detiene sin cambiar nada (error 50002). La consulta 4 de verify.sql cuenta lo que
-- se perdería.
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
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    IF EXISTS (SELECT 1 FROM [Orders] WHERE LEN([TableNumber]) > 10)
        THROW 50002, N'Hay pedidos con mesa de más de 10 caracteres: revertir esta migración los cortaría.', 1;
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    ALTER TABLE [Orders] DROP CONSTRAINT [FK_Orders_DiningTables_DiningTableId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    ALTER TABLE [Orders] DROP CONSTRAINT [FK_Orders_DiningTables_OpenTableId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DROP TABLE [DiningTables];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DROP TABLE [KitchenTickets];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DROP TABLE [OrderItemOptions];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DROP TABLE [ProductOptions];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DROP TABLE [ProductOptionGroups];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DROP INDEX [IX_Orders_DiningTableId] ON [Orders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DROP INDEX [IX_Orders_OpenTableId] ON [Orders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DROP INDEX [IX_Orders_TenantId_OpenTableId_Open] ON [Orders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'DiningTableId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Orders] DROP COLUMN [DiningTableId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'IsTab');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Orders] DROP COLUMN [IsTab];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'OpenTableId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Orders] DROP COLUMN [OpenTableId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderItems]') AND [c].[name] = N'Round');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [OrderItems] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [OrderItems] DROP COLUMN [Round];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'TableNumber');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [Orders] ALTER COLUMN [TableNumber] nvarchar(10) NULL;
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs';
END;

COMMIT;
GO
