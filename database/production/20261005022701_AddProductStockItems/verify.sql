-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261005003444_AddPaymentTipAndCashReceived; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: la columna, sus índices y su llave. Antes de aplicar no devuelven filas;
--    después, la columna, los dos índices (el de (TenantId, ProductId) único y filtrado) y la
--    llave con SET_NULL (vacío otra vez después de rollback.sql).
SELECT c.[name] AS [Columna], c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.SupplyItems') AND c.[name] = N'ProductId';

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.SupplyItems')
  AND i.[name] IN (N'IX_SupplyItems_ProductId', N'IX_SupplyItems_TenantId_ProductId')
ORDER BY i.[name];

SELECT fk.[name] AS [Llave], fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[name] = N'FK_SupplyItems_Products_ProductId';

-- 3. Después: artículos que son la existencia de un producto, por negocio. Justo después de
--    aplicar no hay ninguno. Antes de rollback.sql muestra los que vuelven a ser insumos.
IF COL_LENGTH(N'dbo.SupplyItems', N'ProductId') IS NOT NULL
    EXEC(N'SELECT t.[Name] AS [Negocio], COUNT(*) AS [ProductosQueControlanExistencias]
FROM [SupplyItems] s
INNER JOIN [Tenants] t ON t.[Id] = s.[TenantId]
WHERE s.[ProductId] IS NOT NULL
GROUP BY t.[Name]
ORDER BY [ProductosQueControlanExistencias] DESC;');

-- 4. Antes y después: productos con una existencia capturada en Products.[StockQuantity], que
--    nunca se movió con las ventas. No cambia: la ficha del producto la ofrece como existencia
--    inicial cuando el negocio empieza a controlarla.
SELECT t.[Name] AS [Negocio], COUNT(*) AS [ProductosConExistenciaCapturada]
FROM [Products] p
INNER JOIN [Tenants] t ON t.[Id] = p.[TenantId]
WHERE p.[StockQuantity] > 0
GROUP BY t.[Name]
ORDER BY [ProductosConExistenciaCapturada] DESC;

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261005022701_AddProductStockItems';
