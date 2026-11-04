-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29J, 20261103170924_AddAppointmentRespondedAt,
-- hasta la migración anterior, 20261102205836_AddAppointmentProblemPhotos.
-- Generado el 2026-11-03 con `dotnet ef migrations script
-- 20261103170924_AddAppointmentRespondedAt 20261102205836_AddAppointmentProblemPhotos --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra el índice y las columnas [Appointments].[RespondedAt] y [RequestedStartAt]: se pierde
-- cuándo se respondió cada solicitud desde la Fase 29J (la consulta 3 de verify.sql las cuenta) y
-- la hora que pidió su cliente. No cambia ninguna cita ni su estado; solo lo que Explorar resume. Córrelo después de regresar el código a master con
-- la Fase 29I, que no conoce la columna. Volver al código anterior funciona también sin este
-- script.
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
    WHERE [MigrationId] = N'20261103170924_AddAppointmentRespondedAt'
)
BEGIN
    DROP INDEX [IX_Appointments_OnlineRequests_CreatedAt] ON [Appointments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103170924_AddAppointmentRespondedAt'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'RequestedStartAt');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Appointments] DROP COLUMN [RequestedStartAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103170924_AddAppointmentRespondedAt'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'RespondedAt');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Appointments] DROP COLUMN [RespondedAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103170924_AddAppointmentRespondedAt'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103170924_AddAppointmentRespondedAt';
END;

COMMIT;
GO

