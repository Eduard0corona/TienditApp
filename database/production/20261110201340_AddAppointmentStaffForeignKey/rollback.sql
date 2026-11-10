-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 31H, 20261110201340_AddAppointmentStaffForeignKey,
-- hasta la migración anterior, 20261110022936_AddUniqueCategoryNamePerTenant.
-- Generado el 2026-11-10 con `dotnet ef migrations script
-- 20261110201340_AddAppointmentStaffForeignKey 20261110022936_AddUniqueCategoryNamePerTenant --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra la llave [FK_Appointments_AspNetUsers_StaffUserId] y su índice: no se pierde ningún dato, y
-- la base vuelve a aceptar la persona de una cita sin revisar que su cuenta exista (como antes).
-- Córrelo después de regresar el código a master con la Fase 31G; volver a ese código funciona
-- también sin este script.
-- Borrar la llave toma el mismo candado de esquema sobre [AspNetUsers] que crearla: mientras tanto
-- esperan las solicitudes con sesión. Con -t 15, si tarda más, sqlcmd lo cancela sin cambiar nada.
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
    WHERE [MigrationId] = N'20261110201340_AddAppointmentStaffForeignKey'
)
BEGIN
    ALTER TABLE [Appointments] DROP CONSTRAINT [FK_Appointments_AspNetUsers_StaffUserId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110201340_AddAppointmentStaffForeignKey'
)
BEGIN
    DROP INDEX [IX_Appointments_StaffUserId] ON [Appointments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110201340_AddAppointmentStaffForeignKey'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110201340_AddAppointmentStaffForeignKey';
END;

COMMIT;
GO
