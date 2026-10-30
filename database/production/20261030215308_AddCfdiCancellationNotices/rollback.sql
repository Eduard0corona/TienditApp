-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28N, 20261030215308_AddCfdiCancellationNotices,
-- hasta la migración anterior, 20261030181829_AddCfdiWhatsApp.
-- Generado el 2026-10-30 con `dotnet ef migrations script
-- 20261030215308_AddCfdiCancellationNotices 20261030181829_AddCfdiWhatsApp --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50047; con -b, sqlcmd termina ahí) si ya hay un aviso de
-- cancelación en [CfdiEmails]: borrar la columna lo convertiría en un correo de los archivos del
-- CFDI y perdería lo que se le avisó al cliente. Entonces solo queda corregir hacia adelante (la
-- consulta 3 de verify.sql los cuenta). Sin ellos, borra la restricción [CK_CfdiEmails_Kind], el
-- valor por omisión y la columna [CfdiEmails].[Kind]. Volver al código anterior (master con la
-- Fase 28M) funciona también sin este script.
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
    WHERE [MigrationId] = N'20261030215308_AddCfdiCancellationNotices'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [CfdiEmails] WHERE [Kind] <> 0)
        THROW 50047, N''Notices of cancelled CFDI are recorded already; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030215308_AddCfdiCancellationNotices'
)
BEGIN
    ALTER TABLE [CfdiEmails] DROP CONSTRAINT [CK_CfdiEmails_Kind];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030215308_AddCfdiCancellationNotices'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CfdiEmails]') AND [c].[name] = N'Kind');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [CfdiEmails] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [CfdiEmails] DROP COLUMN [Kind];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030215308_AddCfdiCancellationNotices'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030215308_AddCfdiCancellationNotices';
END;

COMMIT;
GO

