-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29E, 20261102014702_AddQuoteConversion,
-- hasta la migración anterior, 20261101181148_AddQuoteDeliveries.
-- Generado el 2026-11-02 con `dotnet ef migrations script
-- 20261102014702_AddQuoteConversion 20261101181148_AddQuoteDeliveries --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50053; con -b, sqlcmd termina ahí) en cuanto una cotización
-- aceptada se convirtió en una orden de servicio: se perdería en qué orden se convirtió y lo que
-- aplicó de la visita. Entonces solo queda corregir hacia adelante (la consulta 3 de verify.sql las
-- cuenta). Si no, borra la llave, el índice, la restricción y las tres columnas.
-- Correrlo después de regresar el código a master con la Fase 29D: la guarda no bloquea la tabla,
-- y esta versión podría convertir una cotización entre la guarda y el borrado. Volver al código
-- anterior funciona también sin este script.
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
    WHERE [MigrationId] = N'20261102014702_AddQuoteConversion'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [Quotes] WHERE [OrderId] IS NOT NULL OR [ConvertedAt] IS NOT NULL OR [AppliedVisitCredit] IS NOT NULL)
        THROW 50053, N''Accepted quotes already became service orders; rolling back would lose which order each one became and what it applied of the visit. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102014702_AddQuoteConversion'
)
BEGIN
    ALTER TABLE [Quotes] DROP CONSTRAINT [FK_Quotes_Orders_OrderId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102014702_AddQuoteConversion'
)
BEGIN
    DROP INDEX [IX_Quotes_OrderId] ON [Quotes];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102014702_AddQuoteConversion'
)
BEGIN
    ALTER TABLE [Quotes] DROP CONSTRAINT [CK_Quotes_Conversion];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102014702_AddQuoteConversion'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Quotes]') AND [c].[name] = N'AppliedVisitCredit');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Quotes] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Quotes] DROP COLUMN [AppliedVisitCredit];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102014702_AddQuoteConversion'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Quotes]') AND [c].[name] = N'ConvertedAt');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Quotes] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Quotes] DROP COLUMN [ConvertedAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102014702_AddQuoteConversion'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Quotes]') AND [c].[name] = N'OrderId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Quotes] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Quotes] DROP COLUMN [OrderId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102014702_AddQuoteConversion'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102014702_AddQuoteConversion';
END;

COMMIT;
GO
