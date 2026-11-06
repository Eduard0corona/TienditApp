-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29O, 20261106060148_AddTenantUserQuoteReplyEmails,
-- hasta la migración anterior, 20261105171726_AddTenantUserRequestEmails.
-- Generado el 2026-11-06 con `dotnet ef migrations script
-- 20261106060148_AddTenantUserQuoteReplyEmails 20261105171726_AddTenantUserRequestEmails --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra la columna [TenantUsers].[QuoteReplyEmails]: se pierde quién prendió o apagó los avisos
-- por correo de las respuestas a cotizaciones (la consulta 3 de verify.sql los cuenta). No toca
-- [TenantUsers].[RequestEmails] (Fase 29N) ni cambia ningún miembro, su rol ni sus permisos.
-- Córrelo después de regresar el código a master con la Fase 29N, que no conoce la columna.
-- Volver al código anterior funciona también sin este script.
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
    WHERE [MigrationId] = N'20261106060148_AddTenantUserQuoteReplyEmails'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantUsers]') AND [c].[name] = N'QuoteReplyEmails');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantUsers] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantUsers] DROP COLUMN [QuoteReplyEmails];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106060148_AddTenantUserQuoteReplyEmails'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106060148_AddTenantUserQuoteReplyEmails';
END;

COMMIT;
GO
