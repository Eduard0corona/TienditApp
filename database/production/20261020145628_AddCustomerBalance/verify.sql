-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 26C), la más reciente es
--    20261020055548_AddCustomerCreditReminders. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después:
--    20261020145628_AddCustomerBalance.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla [CustomerBalanceEntries], sus restricciones [CK_CustomerBalanceEntries_Amount]
--    y [CK_CustomerBalanceEntries_Ties] y sus índices [IX_CustomerBalanceEntries_TenantId_Phone_OccurredAt]
--    (con [Amount] incluida), [IX_CustomerBalanceEntries_OrderPaymentId] e
--    [IX_CustomerBalanceEntries_OrderRefundId] (únicos y filtrados). Antes de aplicar y después de
--    rollback.sql: ninguna fila.
SELECT
    t.[name] AS [Tabla],
    (SELECT COUNT(*) FROM sys.columns c WHERE c.[object_id] = t.[object_id]) AS [Columnas]
FROM sys.tables t
WHERE t.[name] = N'CustomerBalanceEntries';

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] IN (N'CK_CustomerBalanceEntries_Amount', N'CK_CustomerBalanceEntries_Ties')
ORDER BY [Restriccion];

SELECT
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.CustomerBalanceEntries') AND i.[name] LIKE N'IX[_]%'
ORDER BY [Indice];

-- 3. Después: cuántos movimientos de saldo a favor hay, y cuántos pagos y reembolsos con saldo a
--    favor (forma de pago 7) y movimientos de caja de lo que un cliente dejó o se le devolvió
--    (tipos 8 y 9). rollback.sql se detiene con 50029 si alguno no es cero. Justo después de
--    aplicar: todos en cero. Antes de aplicar y después de rollback.sql la tabla no existe y la
--    consulta no corre.
IF OBJECT_ID(N'dbo.CustomerBalanceEntries', N'U') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [CustomerBalanceEntries]) AS [Movimientos],
        (SELECT COUNT(DISTINCT CONCAT([TenantId], N''|'', [Phone])) FROM [CustomerBalanceEntries]) AS [Cuentas],
        (SELECT COUNT(*) FROM [OrderPayments] WHERE [Method] = 7) AS [PagosConSaldo],
        (SELECT COUNT(*) FROM [OrderRefunds] WHERE [Method] = 7) AS [ReembolsosASaldo],
        (SELECT COUNT(*) FROM [CashTransactions] WHERE [TransactionType] IN (8, 9) OR [PaymentMethod] = 7) AS [MovimientosDeCaja];');

-- 4. Antes y después de aplicar, igual: los pagos, reembolsos y movimientos de caja de cada forma
--    de pago o tipo. La migración no cambia ninguno.
SELECT N'Pagos' AS [Registro], [Method] AS [FormaOTipo], COUNT(*) AS [Filas], SUM([Amount]) AS [Monto]
FROM [OrderPayments]
GROUP BY [Method]
UNION ALL
SELECT N'Reembolsos', [Method], COUNT(*), SUM([Amount])
FROM [OrderRefunds]
GROUP BY [Method]
UNION ALL
SELECT N'Caja', [TransactionType], COUNT(*), SUM([Amount])
FROM [CashTransactions]
GROUP BY [TransactionType]
ORDER BY [Registro], [FormaOTipo];
