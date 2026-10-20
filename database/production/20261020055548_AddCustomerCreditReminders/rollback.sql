-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 26C, 20261020055548_AddCustomerCreditReminders,
-- hasta la migración anterior, 20261020003342_AddCustomerWriteOffs.
-- Generado el 2026-10-20 con `dotnet ef migrations script
-- 20261020055548_AddCustomerCreditReminders 20261020003342_AddCustomerWriteOffs
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50028; con -b, sqlcmd termina ahí) si algún cliente ya tiene
-- plazo propio, algún negocio encendió los recordatorios del fiado o eligió su plantilla, o ya hay
-- plantillas o mensajes de recordatorio del fiado: se perderían los plazos y quedarían
-- recordatorios que la versión anterior no sabe leer. Entonces solo queda corregir hacia adelante
-- (la consulta 3 de verify.sql los cuenta).
-- Sin ellos, borra las restricciones [CK_Customers_CreditTermDays] y
-- [CK_TenantSettings_CreditReminderEveryDays] y las columnas [Customers].[CreditTermDays],
-- [TenantSettings].[CreditRemindersEnabled], [CreditReminderEveryDays] y
-- [CreditReminderTemplateId]. Los saldos de los pedidos no cambian.
-- Volver al código anterior (master con la Fase 26B) funciona también sin este script: no conoce
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
    WHERE [MigrationId] = N'20261020055548_AddCustomerCreditReminders'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [Customers] WHERE [CreditTermDays] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [TenantSettings] WHERE [CreditRemindersEnabled] = 1 OR [CreditReminderTemplateId] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [MessageTemplates] WHERE [Purpose] = 3)
        OR EXISTS (SELECT 1 FROM [OutgoingMessages] WHERE [Purpose] = 3)
        THROW 50028, N''Businesses gave customers their own credit term or used credit reminders already; rolling back would forget the terms and leave reminders the previous version cannot read. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020055548_AddCustomerCreditReminders'
)
BEGIN
    ALTER TABLE [TenantSettings] DROP CONSTRAINT [CK_TenantSettings_CreditReminderEveryDays];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020055548_AddCustomerCreditReminders'
)
BEGIN
    ALTER TABLE [Customers] DROP CONSTRAINT [CK_Customers_CreditTermDays];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020055548_AddCustomerCreditReminders'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'CreditReminderEveryDays');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [CreditReminderEveryDays];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020055548_AddCustomerCreditReminders'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'CreditReminderTemplateId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [CreditReminderTemplateId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020055548_AddCustomerCreditReminders'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'CreditRemindersEnabled');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [CreditRemindersEnabled];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020055548_AddCustomerCreditReminders'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'CreditTermDays');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [Customers] DROP COLUMN [CreditTermDays];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020055548_AddCustomerCreditReminders'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020055548_AddCustomerCreditReminders';
END;

COMMIT;
GO
