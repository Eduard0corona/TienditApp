-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261006054141_DropPlainRefreshTokenColumns (Fase 10; la Fase 11 no tuvo migraciones); si es
--    anterior, apply.sql también aplicará las que falten (revisa primero el verify.sql de cada
--    carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: las cuatro columnas que la versión nueva deja de usar siguen ahí, igual que
--    antes, al aplicar y al revertir: [StockQuantity] y [LowStockThreshold] (int, aceptan nulos),
--    [LoyaltyPoints] (bigint, sin nulos, con el valor por omisión 0 que reciben las filas nuevas) y
--    [LoyaltyTier] (nvarchar(50), acepta nulos). La siguiente fase las borra.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[is_nullable] AS [AceptaNulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE (c.[object_id] = OBJECT_ID(N'dbo.Products') AND c.[name] IN (N'StockQuantity', N'LowStockThreshold'))
    OR (c.[object_id] = OBJECT_ID(N'dbo.CustomerProfiles') AND c.[name] IN (N'LoyaltyPoints', N'LoyaltyTier'))
ORDER BY [Tabla], [Columna];

-- 3. Antes y después: lo capturado a mano en los productos. No cambia al aplicar ni al revertir.
--    [CapturadaSinControl]: productos con existencia capturada que aún no controlan existencias
--    en inventario; al empezar a controlarlas, la versión nueva ya no la propone como existencia
--    inicial. Anótalo: la siguiente fase borra las columnas, y solo el respaldo de la base que se
--    toma antes de su apply.sql conservará estos valores.
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

-- 4. Antes y después: los puntos globales de los clientes. [ConPuntos] y [ConNivel] deben ser 0:
--    nada los escribe (ADR 0009). No cambian al aplicar ni al revertir.
IF COL_LENGTH(N'dbo.CustomerProfiles', N'LoyaltyPoints') IS NOT NULL
    AND COL_LENGTH(N'dbo.CustomerProfiles', N'LoyaltyTier') IS NOT NULL
    EXEC(N'SELECT
    COUNT(*) AS [Perfiles],
    COUNT(CASE WHEN [LoyaltyPoints] <> 0 THEN 1 END) AS [ConPuntos],
    COUNT([LoyaltyTier]) AS [ConNivel]
FROM [CustomerProfiles];');

-- 5. Después: la migración quedó registrada (vacío antes de aplicar y después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261006101452_StopMappingStockAndLoyaltyColumns';
