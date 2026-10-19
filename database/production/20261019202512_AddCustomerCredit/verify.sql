-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24J), la más reciente es
--    20261019150436_AddOnlineBookingStaff. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261019202512_AddCustomerCredit.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas ([Customers].[CreditLimit] decimal(18,2) con nulos y
--    [Orders].[SoldOnCredit] bit sin nulos con su valor por omisión) y el índice
--    [IX_Orders_TenantId_CustomerPhone]. Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [Nulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE (c.[object_id] = OBJECT_ID(N'dbo.Customers') AND c.[name] = N'CreditLimit')
    OR (c.[object_id] = OBJECT_ID(N'dbo.Orders') AND c.[name] = N'SoldOnCredit')
ORDER BY [Tabla];

SELECT
    i.[name] AS [Indice],
    STRING_AGG(COL_NAME(ic.[object_id], ic.[column_id]), N', ') WITHIN GROUP (ORDER BY ic.[key_ordinal]) AS [Columnas]
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
WHERE i.[object_id] = OBJECT_ID(N'dbo.Orders') AND i.[name] = N'IX_Orders_TenantId_CustomerPhone'
GROUP BY i.[name];

-- 3. Después: cuántos clientes tienen límite de fiado (y en cuántos negocios) y cuántas ventas se
--    cerraron fiadas (rollback.sql se detiene con 50026 si hay alguno). Justo después de aplicar:
--    ninguno. Antes de aplicar y después de rollback.sql las columnas no existen y la consulta no
--    corre.
IF COL_LENGTH(N'dbo.Customers', N'CreditLimit') IS NOT NULL AND COL_LENGTH(N'dbo.Orders', N'SoldOnCredit') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [Customers] WHERE [CreditLimit] IS NOT NULL) AS [ClientesConFiado],
        (SELECT COUNT(DISTINCT [TenantId]) FROM [Customers] WHERE [CreditLimit] IS NOT NULL) AS [Negocios],
        (SELECT COUNT(*) FROM [Orders] WHERE [SoldOnCredit] = 1) AS [VentasFiadas];');

-- 4. Antes y después, igual: los pedidos no cancelados con saldo y con teléfono, entregados (lo que
--    ya se debe y aparece en Clientes › Por cobrar) y abiertos. Solo lee [Orders]: la migración no
--    cambia ningún saldo.
SELECT
    CASE WHEN [Status] IN (6, 7) THEN N'Entregados' ELSE N'Abiertos' END AS [Pedidos],
    COUNT(*) AS [ConSaldo],
    COUNT(DISTINCT CONCAT([TenantId], N'|', [CustomerPhone])) AS [Clientes],
    SUM([Total] - [PaidAmount]) AS [Saldo]
FROM [Orders]
WHERE [Status] <> 99 AND [Total] > [PaidAmount] AND [CustomerPhone] IS NOT NULL AND [CustomerPhone] <> N''
GROUP BY CASE WHEN [Status] IN (6, 7) THEN N'Entregados' ELSE N'Abiertos' END
ORDER BY [Pedidos];
