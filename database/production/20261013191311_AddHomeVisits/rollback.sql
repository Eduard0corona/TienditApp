-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22E, 20261013191311_AddHomeVisits,
-- hasta la migración anterior, 20261013085926_AddCourierSettlements.
-- Generado el 2026-10-13 con `dotnet ef migrations script
-- 20261013191311_AddHomeVisits 20261013085926_AddCourierSettlements
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Se detiene sin cambiar nada (error 50015; con -b, sqlcmd termina ahí) si hay alguna cita a
-- domicilio, algún cargo por visita o alguna cita cobrada: se perdería la dirección de la visita y
-- el cargo acordado, y qué venta cobró cada cita (el total de la venta conserva el cargo, pero ya
-- no se distinguiría). Con ellos solo queda corregir hacia adelante (la consulta 4 de verify.sql
-- los cuenta).
-- Sin ellos, borra la llave de [Appointments] a [Orders], las dos tablas (vacías), la CHECK de
-- [TenantSettings], el índice y las tres columnas ([TenantSettings].[HomeVisitTravelMinutes] con
-- su DEFAULT, [Appointments].[SaleOrderId] en NULL y [Appointments].[Version]). Nada más cambia.
-- Volver al código anterior (master con la Fase 22D) funciona también sin este script: no conoce
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
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [AppointmentVisits])
        OR EXISTS (SELECT 1 FROM [OrderVisitCharges])
        OR EXISTS (SELECT 1 FROM [Appointments] WHERE [SaleOrderId] IS NOT NULL)
        THROW 50015, N''There are home visits or charged appointments; rolling back would lose their addresses, visit charges and sales. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    ALTER TABLE [Appointments] DROP CONSTRAINT [FK_Appointments_Orders_SaleOrderId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    DROP TABLE [AppointmentVisits];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    DROP TABLE [OrderVisitCharges];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    ALTER TABLE [TenantSettings] DROP CONSTRAINT [CK_TenantSettings_HomeVisitTravelMinutes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    DROP INDEX [IX_Appointments_SaleOrderId] ON [Appointments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'HomeVisitTravelMinutes');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [HomeVisitTravelMinutes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'SaleOrderId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Appointments] DROP COLUMN [SaleOrderId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'Version');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Appointments] DROP COLUMN [Version];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits';
END;

COMMIT;
GO
