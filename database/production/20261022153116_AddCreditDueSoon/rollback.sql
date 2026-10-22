-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 26E, 20261022153116_AddCreditDueSoon,
-- hasta la migración anterior, 20261022033401_AddPlatformWithholdingCertificates.
-- Generado el 2026-10-22 con `dotnet ef migrations script
-- 20261022153116_AddCreditDueSoon 20261022033401_AddPlatformWithholdingCertificates
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50034; con -b, sqlcmd termina ahí) si algún negocio encendió
-- el aviso antes de vencer o eligió su plantilla, o ya hay plantillas o avisos con ese propósito
-- (4): quedarían plantillas y mensajes que la versión anterior no sabe leer. Entonces solo queda
-- corregir hacia adelante (la consulta 3 de verify.sql los cuenta).
-- Sin ellos, borra la restricción [CK_TenantSettings_CreditDueSoonDaysBefore] y las columnas
-- [TenantSettings].[CreditDueSoonEnabled], [CreditDueSoonDaysBefore] y [CreditDueSoonTemplateId].
-- Los recordatorios de lo vencido no cambian. Volver al código anterior (master con la Fase 22J)
-- funciona también sin este script: no conoce las columnas.
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
    WHERE [MigrationId] = N'20261022153116_AddCreditDueSoon'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [TenantSettings] WHERE [CreditDueSoonEnabled] = 1 OR [CreditDueSoonTemplateId] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [MessageTemplates] WHERE [Purpose] = 4)
        OR EXISTS (SELECT 1 FROM [OutgoingMessages] WHERE [Purpose] = 4)
        THROW 50034, N''Businesses used the notice before the credit comes due already; rolling back would leave notices and templates the previous version cannot read. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022153116_AddCreditDueSoon'
)
BEGIN
    ALTER TABLE [TenantSettings] DROP CONSTRAINT [CK_TenantSettings_CreditDueSoonDaysBefore];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022153116_AddCreditDueSoon'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'CreditDueSoonDaysBefore');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [CreditDueSoonDaysBefore];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022153116_AddCreditDueSoon'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'CreditDueSoonEnabled');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [CreditDueSoonEnabled];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022153116_AddCreditDueSoon'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'CreditDueSoonTemplateId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [CreditDueSoonTemplateId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022153116_AddCreditDueSoon'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022153116_AddCreditDueSoon';
END;

COMMIT;
GO
