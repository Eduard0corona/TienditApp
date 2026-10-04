-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004133416_AddOrderChannel; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: columnas que cambian o se agregan. Antes de aplicar, las cantidades son
--    int y faltan las claves del SAT; después, las cantidades son decimal(18,3) y aparecen
--    [Unit], [SatProductCode], [SatUnitCode] y [TaxObject] (otra vez int y sin claves después de
--    rollback.sql).
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[precision] AS [Precision], c.[scale] AS [Escala]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.OrderItems')
        AND c.[name] IN (N'Quantity', N'ReturnedQuantity', N'Unit', N'SatProductCode', N'SatUnitCode', N'TaxObject'))
   OR (c.[object_id] = OBJECT_ID(N'dbo.OrderRefundLines') AND c.[name] = N'Quantity')
   OR (c.[object_id] = OBJECT_ID(N'dbo.Products') AND c.[name] IN (N'SatProductCode', N'SatUnitCode', N'TaxObject'))
ORDER BY [Tabla], [Columna];

-- 3. Después: cómo quedaron las líneas ya vendidas, por unidad y clave de unidad del SAT. Las de
--    productos borrados quedan como Unit / H87.
IF COL_LENGTH(N'dbo.OrderItems', N'Unit') IS NOT NULL
    EXEC(N'SELECT [Unit], [SatUnitCode], COUNT(*) AS [Lineas]
FROM [OrderItems]
GROUP BY [Unit], [SatUnitCode]
ORDER BY [Lineas] DESC;');

-- 4. Antes de rollback.sql: ventas y devoluciones con cantidades decimales. Deben ser 0; si no,
--    rollback.sql se detiene sin cambiar nada (error 50001) para no truncarlas.
IF COL_LENGTH(N'dbo.OrderItems', N'Unit') IS NOT NULL
    EXEC(N'SELECT
    (SELECT COUNT(*) FROM [OrderItems]
        WHERE [Quantity] <> FLOOR([Quantity]) OR [ReturnedQuantity] <> FLOOR([ReturnedQuantity])) AS [LineasConDecimales],
    (SELECT COUNT(*) FROM [OrderRefundLines] WHERE [Quantity] <> FLOOR([Quantity])) AS [DevolucionesConDecimales];');

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData';
