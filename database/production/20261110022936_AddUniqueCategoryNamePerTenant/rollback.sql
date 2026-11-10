-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 31B, 20261110022936_AddUniqueCategoryNamePerTenant,
-- hasta la migración anterior, 20261108150147_AddOrderCancellationEmailsAndAnswerReminder.
-- Generado el 2026-11-10 con `dotnet ef migrations script
-- 20261110022936_AddUniqueCategoryNamePerTenant 20261108150147_AddOrderCancellationEmailsAndAnswerReminder --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra el índice único [IX_Categories_TenantId_Name]: no se pierde ningún dato, y la base vuelve a
-- aceptar dos categorías con el mismo nombre en un negocio (la versión anterior no lo revisa).
-- Córrelo después de regresar el código a master con la Fase 31A; volver a ese código funciona
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
    WHERE [MigrationId] = N'20261110022936_AddUniqueCategoryNamePerTenant'
)
BEGIN
    DROP INDEX [IX_Categories_TenantId_Name] ON [Categories];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110022936_AddUniqueCategoryNamePerTenant'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261110022936_AddUniqueCategoryNamePerTenant';
END;

COMMIT;
GO

