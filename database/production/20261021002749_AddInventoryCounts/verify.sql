-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 26D), la más reciente es
--    20261020145628_AddCustomerBalance. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261021002749_AddInventoryCounts.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las tablas [InventoryCounts] e [InventoryCountLines], sus restricciones
--    [CK_InventoryCounts_Status], [CK_InventoryCounts_Lines] y [CK_InventoryCountLines_Counted], sus
--    llaves foráneas y sus índices, entre ellos [IX_InventoryCounts_TenantId_InventoryLocationId_Open]
--    (único, filtrado con [Status] = 1), [IX_InventoryCounts_TenantId_CountNumber] e
--    [IX_InventoryCountLines_CountId_SupplyItemId] (únicos). Antes de aplicar y después de
--    rollback.sql: ninguna fila.
SELECT
    t.[name] AS [Tabla],
    (SELECT COUNT(*) FROM sys.columns c WHERE c.[object_id] = t.[object_id]) AS [Columnas]
FROM sys.tables t
WHERE t.[name] IN (N'InventoryCounts', N'InventoryCountLines')
ORDER BY [Tabla];

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] IN (N'CK_InventoryCounts_Status', N'CK_InventoryCounts_Lines', N'CK_InventoryCountLines_Counted')
ORDER BY [Restriccion];

SELECT
    OBJECT_NAME(fk.[parent_object_id]) AS [Tabla],
    fk.[name] AS [LlaveForanea],
    OBJECT_NAME(fk.[referenced_object_id]) AS [Referencia]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] IN (OBJECT_ID(N'dbo.InventoryCounts'), OBJECT_ID(N'dbo.InventoryCountLines'))
ORDER BY [LlaveForanea];

SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] IN (OBJECT_ID(N'dbo.InventoryCounts'), OBJECT_ID(N'dbo.InventoryCountLines'))
    AND i.[name] LIKE N'IX[_]%'
ORDER BY [Indice];

-- 3. Después: cuántos conteos hay de cada estado (1 abierto, 2 aplicado, 3 cancelado), con cuántos
--    artículos, y cuántos ajustes de conteos (referencia externa COUNT:). rollback.sql se detiene con
--    50030 si hay algún conteo. Justo después de aplicar: ninguno. Antes de aplicar y después de
--    rollback.sql las tablas no existen y la consulta no corre.
IF OBJECT_ID(N'dbo.InventoryCounts', N'U') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [InventoryCounts] WHERE [Status] = 1) AS [Abiertos],
        (SELECT COUNT(*) FROM [InventoryCounts] WHERE [Status] = 2) AS [Aplicados],
        (SELECT COUNT(*) FROM [InventoryCounts] WHERE [Status] = 3) AS [Cancelados],
        (SELECT COUNT(*) FROM [InventoryCountLines]) AS [Articulos],
        (SELECT COUNT(*) FROM [InventoryMovements] WHERE [ExternalReference] LIKE N''COUNT:%'') AS [AjustesDeConteos];');

-- 4. Antes y después de aplicar, igual: los movimientos de inventario de cada tipo y las
--    existencias. La migración no cambia ninguno.
SELECT N'Movimientos' AS [Registro], [MovementType] AS [Tipo], COUNT(*) AS [Filas], SUM([TotalAmount]) AS [Monto]
FROM [InventoryMovements]
GROUP BY [MovementType]
UNION ALL
SELECT N'Existencias', NULL, COUNT(*), SUM([OnHandQuantity])
FROM [InventoryStocks]
ORDER BY [Registro], [Tipo];
