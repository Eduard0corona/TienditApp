-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 10, 20261006054141_DropPlainRefreshTokenColumns,
-- hasta 20261005175643_StopUsingPlainRefreshTokenValues.
-- Generado el 2026-10-06 con `dotnet ef migrations script
-- 20261006054141_DropPlainRefreshTokenColumns 20261005175643_StopUsingPlainRefreshTokenValues
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Crea otra vez [Token] y [ReplacedByToken] (nvarchar(500), aceptan nulos, vacías) y el índice
-- IX_RefreshTokens_Token. Los valores no vuelven ni hacen falta: los tokens se buscan por su hash.
-- Solo hace falta para volver a la Fase 8, que lee esas columnas (trata el NULL como "sin valor en
-- texto"): primero este script, después el código de la Fase 8. Volver a la Fase 9 no lo necesita.
-- Si después aplicas apply.sql otra vez, las borra de nuevo.
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
    WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns'
)
BEGIN
    ALTER TABLE [RefreshTokens] ADD [Token] nvarchar(500) NULL;
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns'
)
BEGIN
    ALTER TABLE [RefreshTokens] ADD [ReplacedByToken] nvarchar(500) NULL;
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_Token] ON [RefreshTokens] ([Token]);
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns';
END;

COMMIT;
GO
