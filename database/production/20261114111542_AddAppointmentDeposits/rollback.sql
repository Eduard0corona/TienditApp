-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 31O, 20261114111542_AddAppointmentDeposits, hasta
-- la migración anterior, 20261113033616_AddQuoteDeposits.
-- Generado el 2026-11-14 con `dotnet ef migrations script
-- 20261114111542_AddAppointmentDeposits 20261113033616_AddQuoteDeposits --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- En su transacción: borra la llave, los índices, las restricciones y las cinco columnas de la 31O,
-- y deja [CK_OnlineCheckouts_Outcome] y [CK_OnlinePaymentRefunds_Link] como estaban. Antes se detiene
-- con el error 50063, sin cambiar nada, si un negocio pide anticipo para reservar o un cliente pagó
-- (o fue a pagar) uno: se perdería. La consulta 2 de verify.sql los cuenta. Un anticipo pagado es
-- dinero que se movió (retenido, en una venta, devuelto o con el negocio): con uno así, no reviertas;
-- la consulta 2 dice qué hacer en cada caso.
-- Córrelo después de regresar el código a master con la Fase 31N; volver a ese código funciona
-- también sin este script. Toma los mismos candados de esquema que apply.sql. Con -t 15, si tarda
-- más, sqlcmd lo cancela sin cambiar nada.
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
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [OnlineCheckouts] WITH (TABLOCKX, HOLDLOCK) WHERE [ForAppointment] = 1)
        OR EXISTS (SELECT 1 FROM [Appointments] WITH (TABLOCKX, HOLDLOCK) WHERE [DepositAmount] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [TenantSettings] WITH (TABLOCKX, HOLDLOCK) WHERE [BookingDepositPercent] IS NOT NULL)
        THROW 50063, N''A business asks a deposit to book appointments online or a customer paid one; rolling back would lose it (query 2 of verify.sql).'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT [FK_OnlineCheckouts_Appointments_AppointmentId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    ALTER TABLE [TenantSettings] DROP CONSTRAINT [CK_TenantSettings_BookingDepositPercent];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    ALTER TABLE [OnlinePaymentRefunds] DROP CONSTRAINT [CK_OnlinePaymentRefunds_Link];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    DROP INDEX [IX_OnlineCheckouts_AppointmentId] ON [OnlineCheckouts];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT [CK_OnlineCheckouts_Appointment];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT [CK_OnlineCheckouts_Outcome];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    DROP INDEX [IX_Appointments_DepositHeld_Requests] ON [Appointments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    ALTER TABLE [Appointments] DROP CONSTRAINT [CK_Appointments_Deposit];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'BookingDepositPercent');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [BookingDepositPercent];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OnlineCheckouts]') AND [c].[name] = N'AppointmentId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OnlineCheckouts] DROP COLUMN [AppointmentId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OnlineCheckouts]') AND [c].[name] = N'ForAppointment');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [OnlineCheckouts] DROP COLUMN [ForAppointment];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'DepositAmount');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [Appointments] DROP COLUMN [DepositAmount];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'DepositStatus');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [Appointments] DROP COLUMN [DepositStatus];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    EXEC(N'ALTER TABLE [OnlinePaymentRefunds] ADD CONSTRAINT [CK_OnlinePaymentRefunds_Link] CHECK (([Reason] IN (1, 2) AND [CheckoutId] IS NOT NULL AND [OrderId] IS NULL AND [OrderRefundId] IS NULL) OR ([Reason] IN (3, 4, 5, 6) AND [CheckoutId] IS NULL AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    EXEC(N'ALTER TABLE [OnlineCheckouts] ADD CONSTRAINT [CK_OnlineCheckouts_Outcome] CHECK (([Status] = 0 AND ([Request] IS NOT NULL OR [QuoteId] IS NOT NULL) AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 1 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 2 AND [Request] IS NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NULL AND [Failure] IS NOT NULL AND [Failure] BETWEEN 1 AND 4))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114111542_AddAppointmentDeposits';
END;

COMMIT;
GO
