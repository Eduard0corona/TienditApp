-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 24K, 20261022213253_AddOnlinePaymentReconciliation,
-- hasta la migración anterior, 20261022153116_AddCreditDueSoon.
-- Generado el 2026-10-22 con `dotnet ef migrations script
-- 20261022213253_AddOnlinePaymentReconciliation 20261022153116_AddCreditDueSoon --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50035; con -b, sqlcmd termina ahí) si ya hay devoluciones
-- registradas desde Mercado Pago (motivos 4, 5 y 6), rechazadas que el negocio hizo desde su cuenta
-- (estado 3) o disputas en los cobros: quedarían reembolsos que la versión anterior no sabe leer, y
-- borrarlos perdería por qué se reembolsó un pedido. Entonces solo queda corregir hacia adelante (la
-- consulta 4 de verify.sql los cuenta).
-- Sin ellos, borra los dos índices únicos, [CK_OnlinePaymentRefunds_MadeInMercadoPago],
-- [IX_OnlineCheckouts_TenantId_Dispute], [CK_OnlineCheckouts_Dispute] y las columnas
-- [OnlineCheckouts].[Dispute], [DisputeUpdatedAt] y [OnlinePaymentAccounts].[PaymentsReviewedUntil],
-- y regresa [CK_OnlinePaymentRefunds_Status], [CK_OnlinePaymentRefunds_Link] y
-- [CK_OnlinePaymentRefunds_Settled] a como estaban. Volver al código anterior (master con la Fase
-- 26E) funciona también sin este script.
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
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [OnlinePaymentRefunds] WHERE [Status] = 3 OR [Reason] IN (4, 5, 6))
        OR EXISTS (SELECT 1 FROM [OnlineCheckouts] WHERE [Dispute] <> 0)
        THROW 50035, N''Refunds and disputes from Mercado Pago are on the orders already; rolling back would leave refunds the previous version cannot read. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    DROP INDEX [IX_OnlinePaymentRefunds_TenantId_PaymentId_Chargeback] ON [OnlinePaymentRefunds];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    DROP INDEX [IX_OnlinePaymentRefunds_TenantId_ProviderRefundId] ON [OnlinePaymentRefunds];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    ALTER TABLE [OnlinePaymentRefunds] DROP CONSTRAINT [CK_OnlinePaymentRefunds_Link];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    ALTER TABLE [OnlinePaymentRefunds] DROP CONSTRAINT [CK_OnlinePaymentRefunds_MadeInMercadoPago];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    ALTER TABLE [OnlinePaymentRefunds] DROP CONSTRAINT [CK_OnlinePaymentRefunds_Settled];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    ALTER TABLE [OnlinePaymentRefunds] DROP CONSTRAINT [CK_OnlinePaymentRefunds_Status];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    DROP INDEX [IX_OnlineCheckouts_TenantId_Dispute] ON [OnlineCheckouts];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT [CK_OnlineCheckouts_Dispute];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OnlinePaymentAccounts]') AND [c].[name] = N'PaymentsReviewedUntil');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OnlinePaymentAccounts] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OnlinePaymentAccounts] DROP COLUMN [PaymentsReviewedUntil];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OnlineCheckouts]') AND [c].[name] = N'Dispute');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OnlineCheckouts] DROP COLUMN [Dispute];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OnlineCheckouts]') AND [c].[name] = N'DisputeUpdatedAt');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [OnlineCheckouts] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [OnlineCheckouts] DROP COLUMN [DisputeUpdatedAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    EXEC(N'ALTER TABLE [OnlinePaymentRefunds] ADD CONSTRAINT [CK_OnlinePaymentRefunds_Link] CHECK (([Reason] IN (1, 2) AND [CheckoutId] IS NOT NULL AND [OrderId] IS NULL AND [OrderRefundId] IS NULL) OR ([Reason] = 3 AND [CheckoutId] IS NULL AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    EXEC(N'ALTER TABLE [OnlinePaymentRefunds] ADD CONSTRAINT [CK_OnlinePaymentRefunds_Settled] CHECK (([Status] = 0 AND [SettledAt] IS NULL AND [FailureMessage] IS NULL) OR ([Status] = 1 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NULL) OR ([Status] = 2 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NOT NULL))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    EXEC(N'ALTER TABLE [OnlinePaymentRefunds] ADD CONSTRAINT [CK_OnlinePaymentRefunds_Status] CHECK ([Status] IN (0, 1, 2))');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022213253_AddOnlinePaymentReconciliation';
END;

COMMIT;
GO
