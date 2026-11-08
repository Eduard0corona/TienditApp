-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 30B, 20261108150147_AddOrderCancellationEmailsAndAnswerReminder,
-- hasta la migración anterior, 20261107190735_AddAppointmentMarkedFakeAt.
-- Generado el 2026-11-08 con `dotnet ef migrations script
-- 20261108150147_AddOrderCancellationEmailsAndAnswerReminder 20261107190735_AddAppointmentMarkedFakeAt --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra la restricción y la columna [TenantSettings].[AnswerReminderMinutes] y la columna
-- [TenantUsers].[OrderCancellationEmails]: se
-- pierde cuánto antes eligió cada negocio su recordatorio (vuelve a ser una hora) y quién eligió
-- recibir los pedidos cancelados (la consulta 3 de verify.sql los cuenta). No cambia ningún pedido,
-- cita ni membresía. Córrelo después de regresar el código a master con la Fase 30A, que no conoce
-- las columnas. Volver al código anterior funciona también sin este script.
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
    WHERE [MigrationId] = N'20261108150147_AddOrderCancellationEmailsAndAnswerReminder'
)
BEGIN
    ALTER TABLE [TenantSettings] DROP CONSTRAINT [CK_TenantSettings_AnswerReminderMinutes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150147_AddOrderCancellationEmailsAndAnswerReminder'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantUsers]') AND [c].[name] = N'OrderCancellationEmails');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantUsers] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantUsers] DROP COLUMN [OrderCancellationEmails];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150147_AddOrderCancellationEmailsAndAnswerReminder'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'AnswerReminderMinutes');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [AnswerReminderMinutes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150147_AddOrderCancellationEmailsAndAnswerReminder'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150147_AddOrderCancellationEmailsAndAnswerReminder';
END;

COMMIT;
GO
