-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 32B, 20261115151611_AddAssistantOrders, hasta la
-- migración anterior, 20261114111542_AddAppointmentDeposits.
-- Generado el 2026-11-15 con `dotnet ef migrations script
-- 20261115151611_AddAssistantOrders 20261114111542_AddAppointmentDeposits --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- En su transacción: borra la tabla [AssistantOrders] y la columna [TenantSettings].[AssistantOrders].
-- Antes se detiene con el error 50064, sin cambiar nada, si un cliente ya envió un pedido desde el
-- enlace de un asistente, si hay enlaces todavía abiertos o si un negocio apagó estos pedidos: se
-- perdería. La consulta 2 de verify.sql los cuenta y dice qué hacer.
-- Córrelo después de regresar el código a master con la Fase 32A; volver a ese código funciona
-- también sin este script. Toma los mismos candados de esquema que apply.sql. Con -t 15, si tarda
-- más, sqlcmd lo cancela sin cambiar nada.
-- Idempotente: no hace nada si la migración no está aplicada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -t 15 -i rollback.sql
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
    WHERE [MigrationId] = N'20261115151611_AddAssistantOrders'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [AssistantOrders] WITH (TABLOCKX, HOLDLOCK) WHERE [Status] = 1 OR [ExpiresAt] > SYSDATETIMEOFFSET())
        OR EXISTS (SELECT 1 FROM [TenantSettings] WITH (TABLOCKX, HOLDLOCK) WHERE [AssistantOrders] = 0)
        THROW 50064, N''Customers sent orders from an assistant, links are still open or a business turned these orders off; rolling back would lose it (query 2 of verify.sql). Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115151611_AddAssistantOrders'
)
BEGIN
    DROP TABLE [AssistantOrders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115151611_AddAssistantOrders'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'AssistantOrders');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [AssistantOrders];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115151611_AddAssistantOrders'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115151611_AddAssistantOrders';
END;

COMMIT;
GO
