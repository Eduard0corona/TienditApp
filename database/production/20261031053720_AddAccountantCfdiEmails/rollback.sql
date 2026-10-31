-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28O, 20261031053720_AddAccountantCfdiEmails,
-- hasta la migración anterior, 20261030215308_AddCfdiCancellationNotices.
-- Generado el 2026-10-31 con `dotnet ef migrations script
-- 20261031053720_AddAccountantCfdiEmails 20261030215308_AddCfdiCancellationNotices --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50048; con -b, sqlcmd termina ahí) si ya hay un envío en
-- [CfdiMonthEmails]: borrar la tabla perdería lo que se le mandó al contador. Entonces solo queda
-- corregir hacia adelante (la consulta 3 de verify.sql los cuenta). Sin envíos, borra la tabla
-- [CfdiMonthEmails], la restricción [CK_InvoicingProfiles_Accountant] y las columnas
-- [InvoicingProfiles].[AccountantEmail] y [SendsMonthToAccountant] con su valor por omisión: el
-- correo del contador guardado se pierde y hay que escribirlo otra vez si se vuelve a aplicar.
-- Correrlo después de regresar el código a master con la Fase 28N: la guarda no bloquea la tabla,
-- y esta versión podría guardar un envío entre la guarda y el borrado. Volver al código anterior
-- funciona también sin este script.
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
    WHERE [MigrationId] = N'20261031053720_AddAccountantCfdiEmails'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [CfdiMonthEmails])
        THROW 50048, N''Emails of CFDI to the accountant are recorded already; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031053720_AddAccountantCfdiEmails'
)
BEGIN
    DROP TABLE [CfdiMonthEmails];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031053720_AddAccountantCfdiEmails'
)
BEGIN
    ALTER TABLE [InvoicingProfiles] DROP CONSTRAINT [CK_InvoicingProfiles_Accountant];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031053720_AddAccountantCfdiEmails'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoicingProfiles]') AND [c].[name] = N'AccountantEmail');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [InvoicingProfiles] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [InvoicingProfiles] DROP COLUMN [AccountantEmail];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031053720_AddAccountantCfdiEmails'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoicingProfiles]') AND [c].[name] = N'SendsMonthToAccountant');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [InvoicingProfiles] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [InvoicingProfiles] DROP COLUMN [SendsMonthToAccountant];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031053720_AddAccountantCfdiEmails'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031053720_AddAccountantCfdiEmails';
END;

COMMIT;
GO
