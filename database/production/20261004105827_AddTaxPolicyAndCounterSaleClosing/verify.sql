-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Si la más reciente es anterior a
--    20261004090406_AllowWalkInSalesWithoutCustomer, el script también aplicará las que
--    falten (revisa primero el verify.sql de esa carpeta).
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes: impuesto de las órdenes existentes. [FueraDel16] debe ser 0: todas se calcularon
--    con 16 % sobre el subtotal, que es la tasa que apply.sql registra en sus artículos.
SELECT
    COUNT(*) AS [Ordenes],
    SUM(CASE WHEN ABS([Tax] - [SubTotal] * 0.16) > 0.005 THEN 1 ELSE 0 END) AS [FueraDel16]
FROM [Orders];

-- 3. Antes y después: columnas nuevas. Antes de aplicar no devuelve filas; después, ocho
--    (vacío otra vez después de rollback.sql).
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings')
        AND c.[name] IN (N'PricesIncludeTax', N'DefaultTaxRate', N'AutoCloseCounterSales', N'OnboardingCompletedAt'))
   OR (c.[object_id] = OBJECT_ID(N'dbo.Products') AND c.[name] = N'IsTaxExempt')
   OR (c.[object_id] = OBJECT_ID(N'dbo.Orders') AND c.[name] = N'PricesIncludeTax')
   OR (c.[object_id] = OBJECT_ID(N'dbo.OrderItems') AND c.[name] IN (N'TaxRate', N'IsTaxExempt'))
ORDER BY [Tabla], [Columna];

-- 4. Después: política de cada negocio. Justo después de aplicar, todos tienen
--    [PricesIncludeTax] = 0, [DefaultTaxRate] = 16.00 y [AutoCloseCounterSales] = 1; los que
--    ya tenían productos u órdenes tienen [OnboardingCompletedAt]. Antes de aplicar (o
--    después de rollback.sql) no devuelve nada: EXEC evita que la consulta falle sin columnas.
IF COL_LENGTH(N'dbo.TenantSettings', N'PricesIncludeTax') IS NOT NULL
    EXEC(N'SELECT
    [PricesIncludeTax],
    [DefaultTaxRate],
    [AutoCloseCounterSales],
    COUNT(*) AS [Negocios],
    SUM(CASE WHEN [OnboardingCompletedAt] IS NULL THEN 0 ELSE 1 END) AS [ArranqueTerminado]
FROM [TenantSettings]
GROUP BY [PricesIncludeTax], [DefaultTaxRate], [AutoCloseCounterSales];');

-- 5. Después: artículos vendidos por tasa. Justo después de aplicar, todos en 16.00 y sin
--    exentos; las órdenes nuevas registran la tasa de cada producto.
IF COL_LENGTH(N'dbo.OrderItems', N'TaxRate') IS NOT NULL
    EXEC(N'SELECT [TaxRate], [IsTaxExempt], COUNT(*) AS [Articulos]
FROM [OrderItems]
GROUP BY [TaxRate], [IsTaxExempt]
ORDER BY [TaxRate] DESC;');

-- 6. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing';
