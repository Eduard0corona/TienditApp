-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004123654_CashSessionsPerCashier; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: columnas y tablas nuevas. Antes de aplicar no devuelve filas; después,
--    cuatro (vacío otra vez después de rollback.sql).
SELECT N'Columna' AS [Tipo], OBJECT_NAME(c.[object_id]) + N'.' + c.[name] AS [Nombre]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.Orders') AND c.[name] = N'RefundedAmount')
   OR (c.[object_id] = OBJECT_ID(N'dbo.OrderItems') AND c.[name] = N'ReturnedQuantity')
UNION ALL
SELECT N'Tabla', [name]
FROM sys.tables
WHERE [name] IN (N'OrderRefunds', N'OrderRefundLines');

-- 3. Después: justo después de aplicar todo está en cero; con el uso, reembolsos por forma de
--    pago y día (UTC) para cuadrarlos con el corte de caja.
IF OBJECT_ID(N'dbo.OrderRefunds') IS NOT NULL
    EXEC(N'SELECT CAST([RefundedAt] AS date) AS [Dia], [Method] AS [Forma], COUNT(*) AS [Reembolsos], SUM([Amount]) AS [Monto]
FROM [OrderRefunds]
GROUP BY CAST([RefundedAt] AS date), [Method]
ORDER BY [Dia] DESC, [Forma];');

-- 4. Después: ninguna orden puede haber devuelto más de lo que cobró. Debe estar vacío.
IF COL_LENGTH(N'dbo.Orders', N'RefundedAmount') IS NOT NULL
    EXEC(N'SELECT [Id], [OrderNumber], [PaidAmount], [RefundedAmount]
FROM [Orders]
WHERE [RefundedAmount] > [PaidAmount] OR [RefundedAmount] < 0;');

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004125133_AddOrderRefunds';
