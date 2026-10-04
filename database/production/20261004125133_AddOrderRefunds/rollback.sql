-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004125133_AddOrderRefunds.
-- Generado el 2026-10-04 con `dotnet ef migrations script
-- 20261004125133_AddOrderRefunds 20261004123654_CashSessionsPerCashier
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra OrderRefunds, OrderRefundLines, Orders.[RefundedAmount] y OrderItems.[ReturnedQuantity].
-- Se pierde el historial de reembolsos (motivo, forma, quién y cuándo) y las unidades
-- devueltas. Los movimientos de caja de tipo reembolso y los movimientos de inventario de las
-- devoluciones se conservan, así que el efectivo y las existencias siguen cuadrando.
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
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    DROP TABLE [OrderRefundLines];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    DROP TABLE [OrderRefunds];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'RefundedAmount');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Orders] DROP COLUMN [RefundedAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderItems]') AND [c].[name] = N'ReturnedQuantity');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OrderItems] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OrderItems] DROP COLUMN [ReturnedQuantity];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds';
END;

COMMIT;
GO
