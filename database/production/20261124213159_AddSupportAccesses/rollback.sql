-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 32G, 20261124213159_AddSupportAccesses, hasta la
-- migración anterior, 20261116030110_AddAssistantConnections.
-- Generado el 2026-11-24 con `dotnet ef migrations script
-- 20261124213159_AddSupportAccesses 20261116030110_AddAssistantConnections --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- En su transacción: borra la tabla [SupportAccesses]. Antes se detiene con el error 50066, sin
-- cambiar nada, si ya hay un acceso de soporte, abierto o no: es el historial de quién entró a qué
-- negocio, por qué y hasta cuándo. La consulta 2 de verify.sql los cuenta y dice qué hacer.
-- Córrelo después de regresar el código a master con la Fase 32F; volver a ese código funciona
-- también sin este script. Con -t 15, si tarda más, sqlcmd lo cancela sin cambiar nada.
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
    WHERE [MigrationId] = N'20261124213159_AddSupportAccesses'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [SupportAccesses] WITH (TABLOCKX, HOLDLOCK))
        THROW 50066, N''There are support accesses; rolling back would delete their history (query 2 of verify.sql). Go back to the previous code without it.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124213159_AddSupportAccesses'
)
BEGIN
    DROP TABLE [SupportAccesses];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124213159_AddSupportAccesses'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124213159_AddSupportAccesses';
END;

COMMIT;
GO

