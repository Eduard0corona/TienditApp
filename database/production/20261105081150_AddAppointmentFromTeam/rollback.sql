-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29M, 20261105081150_AddAppointmentFromTeam,
-- hasta la migración anterior, 20261103170924_AddAppointmentRespondedAt.
-- Generado el 2026-11-05 con `dotnet ef migrations script
-- 20261105081150_AddAppointmentFromTeam 20261103170924_AddAppointmentRespondedAt --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra el índice y las columnas [Appointments].[FromTeam] y [RequestPhone]: se pierde cuáles
-- solicitudes vinieron del equipo desde la Fase 29M y el teléfono con que llegó cada una (la
-- consulta 3 de verify.sql las cuenta). No cambia ninguna cita ni su estado; solo lo que Explorar
-- resume. Córrelo después de regresar el código a master con la Fase 29L, que no conoce las
-- columnas. Volver al código anterior funciona también sin este script.
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
    WHERE [MigrationId] = N'20261105081150_AddAppointmentFromTeam'
)
BEGIN
    DROP INDEX [IX_Appointments_OnlineRequests_Customer] ON [Appointments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081150_AddAppointmentFromTeam'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'FromTeam');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Appointments] DROP COLUMN [FromTeam];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081150_AddAppointmentFromTeam'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'RequestPhone');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Appointments] DROP COLUMN [RequestPhone];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081150_AddAppointmentFromTeam'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081150_AddAppointmentFromTeam';
END;

COMMIT;
GO

