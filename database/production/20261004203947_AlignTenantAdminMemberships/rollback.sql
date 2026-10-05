-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004203947_AlignTenantAdminMemberships.
-- Generado el 2026-10-05 con `dotnet ef migrations script
-- 20261004203947_AlignTenantAdminMemberships 20261004203922_AddOrderNotificationPolicy
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- No cambia datos (el Down está vacío): con el código anterior esas cuentas administraban de
-- todos modos por su rol. Solo quita el registro de la migración.
-- Idempotente: no hace nada si la migración no está aplicada.
--
-- Ejecútalo solo después de volver a desplegar el código anterior, y revierte primero las
-- migraciones posteriores (cada carpeta tiene su rollback.sql), de la más nueva a la más vieja.
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
    WHERE [MigrationId] = N'20261004203947_AlignTenantAdminMemberships'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004203947_AlignTenantAdminMemberships';
END;

COMMIT;
GO
