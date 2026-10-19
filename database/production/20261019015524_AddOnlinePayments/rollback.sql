-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24I, 20261019015524_AddOnlinePayments,
-- hasta la migración anterior, 20261018141704_AddBusinessReviews.
-- Generado el 2026-10-19 con `dotnet ef migrations script
-- 20261019015524_AddOnlinePayments 20261018141704_AddBusinessReviews
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50024; con -b, sqlcmd termina ahí) si algún cliente pagó en
-- línea o empezó a hacerlo: se perdería el registro de sus pagos y de lo que se le devolvió.
-- Entonces solo queda corregir hacia adelante (la consulta 3 de verify.sql los cuenta). Sin pagos,
-- borra las cuatro tablas y [DailySalesSnapshots].[OnlineAmount] (sin pagos en línea, en 0); las
-- cuentas conectadas se pierden y cada negocio las conecta otra vez.
-- Nada más cambia.
-- Volver al código anterior (master con la Fase 24H) funciona también sin este script: no conoce
-- las tablas. Un pedido ya pagado en línea conserva su pago «En línea» (6), que esa versión muestra
-- como un método sin nombre.
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
    WHERE [MigrationId] = N'20261019015524_AddOnlinePayments'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [OnlineCheckouts]) OR EXISTS (SELECT 1 FROM [OnlinePaymentRefunds])
        THROW 50024, N'Customers paid online; rolling back would delete the record of their payments and refunds. Roll forward instead.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019015524_AddOnlinePayments'
)
BEGIN
    DROP TABLE [OnlinePaymentAccounts];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019015524_AddOnlinePayments'
)
BEGIN
    DROP TABLE [OnlinePaymentAuthorizations];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019015524_AddOnlinePayments'
)
BEGIN
    DROP TABLE [OnlinePaymentRefunds];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019015524_AddOnlinePayments'
)
BEGIN
    DROP TABLE [OnlineCheckouts];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019015524_AddOnlinePayments'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[DailySalesSnapshots]') AND [c].[name] = N'OnlineAmount');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [DailySalesSnapshots] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [DailySalesSnapshots] DROP COLUMN [OnlineAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019015524_AddOnlinePayments'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261019015524_AddOnlinePayments';
END;

COMMIT;
GO
