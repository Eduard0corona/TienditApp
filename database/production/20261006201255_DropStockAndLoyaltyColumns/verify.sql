-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261006101452_StopMappingStockAndLoyaltyColumns (Fase 12). Si es anterior, no apliques
--    apply.sql mientras corra esa versión, que lee las columnas: despliega antes la Fase 12.
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes: lo que depende de las cuatro columnas. Debe salir solo la restricción del valor por
--    omisión de [LoyaltyPoints], que apply.sql borra, y ningún índice, estadística ni objeto.
--    Hay que resolver antes de aplicar: un índice; una estadística con [Automatica] = 0 (creada a
--    mano: bloquea el borrado, se quita con DROP STATISTICS); o una vista, función o procedimiento
--    que use las tablas. Las estadísticas automáticas se borran solas con la columna.
--    Después de aplicar: ninguna fila.
SELECT OBJECT_NAME(dc.[parent_object_id]) AS [Tabla], c.[name] AS [Columna], dc.[name] AS [PorOmision]
FROM sys.default_constraints dc
INNER JOIN sys.columns c ON c.[object_id] = dc.[parent_object_id] AND c.[column_id] = dc.[parent_column_id]
WHERE (dc.[parent_object_id] = OBJECT_ID(N'dbo.Products') AND c.[name] IN (N'StockQuantity', N'LowStockThreshold'))
    OR (dc.[parent_object_id] = OBJECT_ID(N'dbo.CustomerProfiles') AND c.[name] IN (N'LoyaltyPoints', N'LoyaltyTier'))
ORDER BY [Tabla], [Columna];

SELECT OBJECT_NAME(ic.[object_id]) AS [Tabla], i.[name] AS [Indice], c.[name] AS [Columna]
FROM sys.index_columns ic
INNER JOIN sys.indexes i ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id]
INNER JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
WHERE (ic.[object_id] = OBJECT_ID(N'dbo.Products') AND c.[name] IN (N'StockQuantity', N'LowStockThreshold'))
    OR (ic.[object_id] = OBJECT_ID(N'dbo.CustomerProfiles') AND c.[name] IN (N'LoyaltyPoints', N'LoyaltyTier'))
ORDER BY [Tabla], [Indice];

SELECT OBJECT_NAME(s.[object_id]) AS [Tabla], s.[name] AS [Estadistica], c.[name] AS [Columna], s.[auto_created] AS [Automatica]
FROM sys.stats s
INNER JOIN sys.stats_columns sc ON sc.[object_id] = s.[object_id] AND sc.[stats_id] = s.[stats_id]
INNER JOIN sys.columns c ON c.[object_id] = sc.[object_id] AND c.[column_id] = sc.[column_id]
WHERE ((s.[object_id] = OBJECT_ID(N'dbo.Products') AND c.[name] IN (N'StockQuantity', N'LowStockThreshold'))
        OR (s.[object_id] = OBJECT_ID(N'dbo.CustomerProfiles') AND c.[name] IN (N'LoyaltyPoints', N'LoyaltyTier')))
    AND NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.[object_id] = s.[object_id] AND i.[index_id] = s.[stats_id])
ORDER BY [Tabla], [Estadistica];

-- (Las tablas mismas no cuentan: sus restricciones e índices filtrados las referencian.)
SELECT OBJECT_SCHEMA_NAME(d.[referencing_id]) + N'.' + OBJECT_NAME(d.[referencing_id]) AS [ObjetoQueUsaLaTabla],
    OBJECT_NAME(d.[referenced_id]) AS [Tabla]
FROM sys.sql_expression_dependencies d
WHERE d.[referenced_id] IN (OBJECT_ID(N'dbo.Products'), OBJECT_ID(N'dbo.CustomerProfiles'))
    AND d.[referencing_id] NOT IN (OBJECT_ID(N'dbo.Products'), OBJECT_ID(N'dbo.CustomerProfiles'))
    AND OBJECTPROPERTY(d.[referencing_id], N'IsTable') = 0;

-- 3. Antes: lo capturado a mano en los productos, que se pierde al aplicar (queda solo en el
--    respaldo). Debe coincidir con la consulta 3 del verify.sql de la Fase 12, salvo productos
--    nuevos o borrados desde entonces: la Fase 12 ya no escribe esas columnas.
--    Después de aplicar no sale: las columnas ya no existen.
IF COL_LENGTH(N'dbo.Products', N'StockQuantity') IS NOT NULL
    AND COL_LENGTH(N'dbo.Products', N'LowStockThreshold') IS NOT NULL
    EXEC(N'SELECT
    COUNT(*) AS [Productos],
    COUNT(p.[StockQuantity]) AS [ConExistenciaCapturada],
    COUNT(p.[LowStockThreshold]) AS [ConMinimoCapturado],
    COUNT(CASE WHEN p.[StockQuantity] IS NOT NULL AND s.[ProductId] IS NULL THEN 1 END) AS [CapturadaSinControl]
FROM [Products] p
LEFT JOIN (
    SELECT DISTINCT [ProductId] FROM [SupplyItems] WHERE [ProductId] IS NOT NULL AND [IsActive] = 1
) s ON s.[ProductId] = p.[Id];');

-- 4. Antes: los puntos globales, que se pierden al aplicar. [ConPuntos] y [ConNivel] deben ser 0:
--    nada los escribe (ADR 0009). Después de aplicar no sale.
IF COL_LENGTH(N'dbo.CustomerProfiles', N'LoyaltyPoints') IS NOT NULL
    AND COL_LENGTH(N'dbo.CustomerProfiles', N'LoyaltyTier') IS NOT NULL
    EXEC(N'SELECT
    COUNT(*) AS [Perfiles],
    COUNT(CASE WHEN [LoyaltyPoints] <> 0 THEN 1 END) AS [ConPuntos],
    COUNT([LoyaltyTier]) AS [ConNivel]
FROM [CustomerProfiles];');

-- 5. Antes y después: las columnas. Antes: las cuatro. Después de aplicar: ninguna. Después de
--    rollback.sql: otra vez las cuatro, vacías ([StockQuantity], [LowStockThreshold] y
--    [LoyaltyTier] en NULL, [LoyaltyPoints] en 0 con su valor por omisión).
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], t.[name] AS [Tipo], c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE (c.[object_id] = OBJECT_ID(N'dbo.Products') AND c.[name] IN (N'StockQuantity', N'LowStockThreshold'))
    OR (c.[object_id] = OBJECT_ID(N'dbo.CustomerProfiles') AND c.[name] IN (N'LoyaltyPoints', N'LoyaltyTier'))
ORDER BY [Tabla], [Columna];

-- 6. Antes y después: las filas. [Productos] y [Perfiles] no cambian al aplicar ni al revertir.
SELECT
    (SELECT COUNT(*) FROM [Products]) AS [Productos],
    (SELECT COUNT(*) FROM [CustomerProfiles]) AS [Perfiles];

-- 7. Después: la migración quedó registrada (vacío antes de aplicar y después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns';
