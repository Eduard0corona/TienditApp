-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22F), la más reciente es
--    20261012005540_AddDeliveryPlatformOrders. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261012112823_AddPlatformPrices.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas de [ProductPlatformPrices] y [ProductOptionPlatformPrices]: [Price] y
--    [PriceDelta] en decimal(18,2), [Channel] int y ninguna acepta nulos. Antes de aplicar y después
--    de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] IN (OBJECT_ID(N'dbo.ProductPlatformPrices'), OBJECT_ID(N'dbo.ProductOptionPlatformPrices'))
ORDER BY [Tabla], c.[column_id];

-- 3. Después: las restricciones CHECK, dos de [ProductPlatformPrices] (_Channel y _Price) y dos de
--    [ProductOptionPlatformPrices] (_Channel y _PriceDelta); las llaves
--    FK_ProductPlatformPrices_Products_ProductId y FK_ProductOptionPlatformPrices_ProductOptions_OptionId,
--    las dos en cascada (CASCADE); y los índices: las dos llaves primarias,
--    IX_ProductPlatformPrices_ProductId_Channel e IX_ProductOptionPlatformPrices_OptionId_Channel
--    ([Unico] 1), e IX_ProductPlatformPrices_TenantId_Channel e
--    IX_ProductOptionPlatformPrices_TenantId_Channel. Antes de aplicar y después de rollback.sql:
--    ninguna fila.
SELECT N'CHECK' AS [Tipo], OBJECT_NAME([parent_object_id]) AS [Tabla], [name] AS [Nombre], NULL AS [Unico],
    [definition] COLLATE DATABASE_DEFAULT AS [Definicion]
FROM sys.check_constraints
WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.ProductPlatformPrices'), OBJECT_ID(N'dbo.ProductOptionPlatformPrices'))
UNION ALL
SELECT N'FK', OBJECT_NAME([parent_object_id]), [name], NULL, [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.ProductPlatformPrices'), OBJECT_ID(N'dbo.ProductOptionPlatformPrices'))
UNION ALL
SELECT N'INDEX', OBJECT_NAME([object_id]), [name], CAST([is_unique] AS int), NULL
FROM sys.indexes
WHERE [object_id] IN (OBJECT_ID(N'dbo.ProductPlatformPrices'), OBJECT_ID(N'dbo.ProductOptionPlatformPrices')) AND [type] > 0
ORDER BY [Tipo], [Tabla], [Nombre];

-- 4. Después de desplegar, y antes de rollback.sql: los precios guardados de productos y de opciones
--    por plataforma ([Canal] 3 Rappi, 4 Uber Eats y 5 DiDi Food) y cuántos negocios los tienen.
--    Con alguna fila, rollback.sql se detiene (error 50009) sin cambiar nada: ninguna bitácora los
--    conserva. Recién aplicado: ninguna fila.
IF OBJECT_ID(N'[ProductPlatformPrices]') IS NOT NULL AND OBJECT_ID(N'[ProductOptionPlatformPrices]') IS NOT NULL
    EXEC(N'SELECT N''Productos'' AS [Tipo], [Channel] AS [Canal], COUNT(*) AS [Precios], COUNT(DISTINCT [TenantId]) AS [Negocios]
FROM [ProductPlatformPrices]
GROUP BY [Channel]
UNION ALL
SELECT N''Opciones'', [Channel], COUNT(*), COUNT(DISTINCT [TenantId])
FROM [ProductOptionPlatformPrices]
GROUP BY [Channel]
ORDER BY [Tipo], [Canal];');

-- 5. Después: consistencia. Las dos consultas deben dar 0 filas: precios cuyo negocio no es el de su
--    producto o el de su opción (el código siempre copia el del producto).
IF OBJECT_ID(N'[ProductPlatformPrices]') IS NOT NULL AND OBJECT_ID(N'[ProductOptionPlatformPrices]') IS NOT NULL
BEGIN
    EXEC(N'SELECT pp.[Id] AS [Precio], pp.[TenantId] AS [Negocio], p.[TenantId] AS [NegocioDelProducto]
FROM [ProductPlatformPrices] pp
INNER JOIN [Products] p ON p.[Id] = pp.[ProductId]
WHERE p.[TenantId] <> pp.[TenantId];');
    EXEC(N'SELECT op.[Id] AS [Precio], op.[TenantId] AS [Negocio], o.[TenantId] AS [NegocioDeLaOpcion]
FROM [ProductOptionPlatformPrices] op
INNER JOIN [ProductOptions] o ON o.[Id] = op.[OptionId]
WHERE o.[TenantId] <> op.[TenantId];');
END;
