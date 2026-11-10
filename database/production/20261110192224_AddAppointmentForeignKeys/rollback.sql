-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 31H, 20261110192224_AddAppointmentForeignKeys,
-- hasta la migración anterior, 20261110022936_AddUniqueCategoryNamePerTenant.
-- Generado el 2026-11-10 con `dotnet ef migrations script
-- 20261110192224_AddAppointmentForeignKeys 20261110022936_AddUniqueCategoryNamePerTenant --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra las llaves [FK_Appointments_AspNetUsers_StaffUserId] y [FK_Appointments_Products_ServiceProductId]
-- y sus índices: la base vuelve a aceptar vínculos de una cita que no apuntan a nada, y borrar un
-- producto ya no vacía el de sus citas (como antes de esta fase). Los vínculos que apply.sql vació
-- se quedan vacíos: apuntaban a un producto o a una cuenta que no existe; los nombres siguen.
-- Córrelo después de regresar el código a master con la Fase 31G; volver a ese código funciona
-- también sin este script.
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
    WHERE [MigrationId] = N'20261110192224_AddAppointmentForeignKeys'
)
BEGIN
    ALTER TABLE [Appointments] DROP CONSTRAINT [FK_Appointments_AspNetUsers_StaffUserId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192224_AddAppointmentForeignKeys'
)
BEGIN
    ALTER TABLE [Appointments] DROP CONSTRAINT [FK_Appointments_Products_ServiceProductId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192224_AddAppointmentForeignKeys'
)
BEGIN
    DROP INDEX [IX_Appointments_ServiceProductId] ON [Appointments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192224_AddAppointmentForeignKeys'
)
BEGIN
    DROP INDEX [IX_Appointments_StaffUserId] ON [Appointments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192224_AddAppointmentForeignKeys'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110192224_AddAppointmentForeignKeys';
END;

COMMIT;
GO

