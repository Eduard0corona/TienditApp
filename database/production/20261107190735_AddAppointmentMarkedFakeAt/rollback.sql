-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29V, 20261107190735_AddAppointmentMarkedFakeAt,
-- hasta la migración anterior, 20261107061906_AddAppointmentAnswerReminderSentAt.
-- Generado el 2026-11-07 con `dotnet ef migrations script
-- 20261107190735_AddAppointmentMarkedFakeAt 20261107061906_AddAppointmentAnswerReminderSentAt --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra las columnas [Appointments].[MarkedFakeAt] y [UnmarkedFakeAt]: se pierde qué solicitudes
-- se marcaron como falsas y cuáles dejaron de estarlo (la consulta 3 de verify.sql las cuenta).
-- Las marcadas se quedan canceladas, sin respuesta, así que tampoco cuentan en Explorar; sus
-- teléfonos vuelven a poder pedir. No cambia ninguna cita ni su estado. Córrelo después de
-- regresar el código a master con la Fase 29U, que no conoce las columnas. Volver al código
-- anterior funciona también sin este script.
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
    WHERE [MigrationId] = N'20261107190735_AddAppointmentMarkedFakeAt'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'MarkedFakeAt');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Appointments] DROP COLUMN [MarkedFakeAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107190735_AddAppointmentMarkedFakeAt'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'UnmarkedFakeAt');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Appointments] DROP COLUMN [UnmarkedFakeAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107190735_AddAppointmentMarkedFakeAt'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261107190735_AddAppointmentMarkedFakeAt';
END;

COMMIT;
GO

