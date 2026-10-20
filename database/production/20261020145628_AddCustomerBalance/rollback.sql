-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 26D, 20261020145628_AddCustomerBalance,
-- hasta la migración anterior, 20261020055548_AddCustomerCreditReminders.
-- Generado el 2026-10-20 con `dotnet ef migrations script
-- 20261020145628_AddCustomerBalance 20261020055548_AddCustomerCreditReminders
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin
-- cambios.
--
-- Se detiene sin cambiar nada (error 50029; con -b, sqlcmd termina ahí) si ya hay movimientos de
-- saldo a favor, pagos o reembolsos con saldo a favor (forma de pago 7), o movimientos de caja de
-- lo que un cliente dejó o se le devolvió (tipos 8 y 9): se perdería lo que los clientes dejaron
-- y quedarían pagos, reembolsos y movimientos de caja que la versión anterior no sabe leer.
-- Entonces solo queda corregir hacia adelante (la consulta 3 de verify.sql los cuenta).
-- Sin ellos, borra la tabla [CustomerBalanceEntries], vacía, con sus restricciones e índices.
-- Volver al código anterior (master con la Fase 26C) funciona también sin este script mientras
-- nadie haya usado el saldo a favor: no conoce la tabla.
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
    WHERE [MigrationId] = N'20261020145628_AddCustomerBalance'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [CustomerBalanceEntries])
        OR EXISTS (SELECT 1 FROM [OrderPayments] WHERE [Method] = 7)
        OR EXISTS (SELECT 1 FROM [OrderRefunds] WHERE [Method] = 7)
        OR EXISTS (SELECT 1 FROM [CashTransactions] WHERE [TransactionType] IN (8, 9) OR [PaymentMethod] = 7)
        THROW 50029, N''Customers left a balance in their favor, or paid or were refunded with it, already; rolling back would forget what customers left and leave payments, refunds and drawer movements the previous version cannot read. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020145628_AddCustomerBalance'
)
BEGIN
    DROP TABLE [CustomerBalanceEntries];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020145628_AddCustomerBalance'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020145628_AddCustomerBalance';
END;

COMMIT;
GO
