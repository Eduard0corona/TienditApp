-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29U, 20261107061906_AddAppointmentAnswerReminderSentAt,
-- hasta la migración anterior, 20261106060148_AddTenantUserQuoteReplyEmails.
-- Generado el 2026-11-07 con `dotnet ef migrations script
-- 20261107061906_AddAppointmentAnswerReminderSentAt 20261106060148_AddTenantUserQuoteReplyEmails --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra la columna [Appointments].[AnswerReminderSentAt]: se pierde a qué solicitudes ya se les
-- mandó su recordatorio (la consulta 3 de verify.sql las cuenta). No cambia ninguna cita, su estado
-- ni su respuesta. Córrelo después de regresar el código a master con la Fase 29T, que no conoce
-- la columna. Volver al código anterior funciona también sin este script.
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
    WHERE [MigrationId] = N'20261107061906_AddAppointmentAnswerReminderSentAt'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'AnswerReminderSentAt');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Appointments] DROP COLUMN [AnswerReminderSentAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107061906_AddAppointmentAnswerReminderSentAt'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107061906_AddAppointmentAnswerReminderSentAt';
END;

COMMIT;
GO

