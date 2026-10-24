-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 28D, 20261024084435_AddCreditNotes,
-- hasta la migración anterior, 20261024014559_AddPaymentComplements.
-- Generado el 2026-10-24 con `dotnet ef migrations script
-- 20261024084435_AddCreditNotes 20261024014559_AddPaymentComplements --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50040; con -b, sqlcmd termina ahí) si ya hay notas de crédito
-- (pedidas, timbradas, rechazadas o canceladas): borrarlas perdería CFDI que el SAT ya tiene.
-- Entonces solo queda corregir hacia adelante (la consulta 3 de verify.sql las cuenta). Sin ellas,
-- borra [CreditNotes] y [InvoicingProfiles].[CreditNoteSerie]. Volver al código anterior (master con
-- la Fase 28C) funciona también sin este script.
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
    WHERE [MigrationId] = N'20261024084435_AddCreditNotes'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [CreditNotes])
        THROW 50040, N''Credit notes are recorded already; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024084435_AddCreditNotes'
)
BEGIN
    DROP TABLE [CreditNotes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024084435_AddCreditNotes'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[InvoicingProfiles]') AND [c].[name] = N'CreditNoteSerie');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [InvoicingProfiles] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [InvoicingProfiles] DROP COLUMN [CreditNoteSerie];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024084435_AddCreditNotes'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024084435_AddCreditNotes';
END;

COMMIT;
GO
