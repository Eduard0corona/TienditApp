-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 26A), la más reciente es
--    20261019202512_AddCustomerCredit. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261020003342_AddCustomerWriteOffs.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas ([Orders].[WrittenOffAmount] decimal(18,2) sin nulos con su
--    valor por omisión y [TenantSettings].[CreditTermDays] int con nulos), la tabla
--    [OrderWriteOffs] y las restricciones [CK_Orders_WrittenOffAmount],
--    [CK_TenantSettings_CreditTermDays] y [CK_OrderWriteOffs_Amount]. Antes de aplicar y después
--    de rollback.sql: ninguna fila.
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
WHERE (c.[object_id] = OBJECT_ID(N'dbo.Orders') AND c.[name] = N'WrittenOffAmount')
    OR (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'CreditTermDays')
ORDER BY [Tabla];

SELECT
    t.[name] AS [Tabla],
    (SELECT COUNT(*) FROM sys.indexes i WHERE i.[object_id] = t.[object_id] AND i.[index_id] > 0) AS [Indices]
FROM sys.tables t
WHERE t.[object_id] = OBJECT_ID(N'dbo.OrderWriteOffs');

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] IN (N'CK_Orders_WrittenOffAmount', N'CK_TenantSettings_CreditTermDays', N'CK_OrderWriteOffs_Amount')
ORDER BY [Tabla];

-- 3. Después: cuánto se ha condonado (en cuántos pedidos y negocios) y cuántos negocios tienen plazo
--    de fiado (rollback.sql se detiene con 50027 si hay alguno). Justo después de aplicar: ninguno.
--    Antes de aplicar y después de rollback.sql la tabla y las columnas no existen y la consulta no
--    corre.
IF OBJECT_ID(N'dbo.OrderWriteOffs') IS NOT NULL AND COL_LENGTH(N'dbo.TenantSettings', N'CreditTermDays') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [OrderWriteOffs]) AS [Condonaciones],
        (SELECT COUNT(DISTINCT [OrderId]) FROM [OrderWriteOffs]) AS [Pedidos],
        (SELECT COUNT(DISTINCT [TenantId]) FROM [OrderWriteOffs]) AS [NegociosQueCondonaron],
        (SELECT ISNULL(SUM([Amount]), 0) FROM [OrderWriteOffs]) AS [Condonado],
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [CreditTermDays] IS NOT NULL) AS [NegociosConPlazo];');

-- 4. Antes y después de aplicar, igual: lo que los clientes deben por la regla de la Fase 26A
--    (pedidos no cancelados, con teléfono y saldo, fiados o entregados), sin restar lo condonado.
--    Solo lee columnas que ya existen: la migración no cambia ningún saldo. Después de condonar,
--    el saldo de Clientes › Por cobrar es este menos lo condonado de la consulta 3.
SELECT
    CASE WHEN [SoldOnCredit] = 1 THEN N'Fiados' ELSE N'Entregados sin cobrar' END AS [Pedidos],
    COUNT(*) AS [ConSaldo],
    COUNT(DISTINCT CONCAT([TenantId], N'|', [CustomerPhone])) AS [Clientes],
    SUM([Total] - [PaidAmount]) AS [Saldo]
FROM [Orders]
WHERE [Status] <> 99 AND [Total] > [PaidAmount] AND [CustomerPhone] IS NOT NULL AND [CustomerPhone] <> N''
    AND ([SoldOnCredit] = 1 OR [Status] IN (6, 7))
GROUP BY CASE WHEN [SoldOnCredit] = 1 THEN N'Fiados' ELSE N'Entregados sin cobrar' END
ORDER BY [Pedidos];
