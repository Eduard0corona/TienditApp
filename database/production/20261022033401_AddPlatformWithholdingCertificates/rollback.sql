-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 22J, 20261022033401_AddPlatformWithholdingCertificates,
-- hasta la migración anterior, 20261022011024_AddPlatformOrderIds.
-- Generado el 2026-10-22 con `dotnet ef migrations script
-- 20261022033401_AddPlatformWithholdingCertificates 20261022011024_AddPlatformOrderIds
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50033; con -b, sqlcmd termina ahí) si algún negocio ya subió
-- un CFDI de retenciones, también uno que después quitó: se perdería lo que dice cada uno y la
-- conciliación no tendría con qué comparar. Entonces solo queda corregir hacia adelante (la
-- consulta 3 de verify.sql los cuenta).
-- Sin ninguno, borra la tabla [PlatformWithholdingCertificates], vacía, con sus restricciones e
-- índices. Volver al código anterior (master con la Fase 22I) funciona también sin este script:
-- no conoce la tabla.
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
    WHERE [MigrationId] = N'20261022033401_AddPlatformWithholdingCertificates'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [PlatformWithholdingCertificates])
        THROW 50033, N'There are CFDI de retenciones of delivery platforms saved; rolling back would delete them. Export and delete them first, or roll forward.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022033401_AddPlatformWithholdingCertificates'
)
BEGIN
    DROP TABLE [PlatformWithholdingCertificates];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022033401_AddPlatformWithholdingCertificates'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022033401_AddPlatformWithholdingCertificates';
END;

COMMIT;
GO
