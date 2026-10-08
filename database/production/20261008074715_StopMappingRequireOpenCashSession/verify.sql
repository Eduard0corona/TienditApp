-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261006201255_DropStockAndLoyaltyColumns (Fase 13; la Fase 14 no tuvo migraciones). Si es
--    anterior, apply.sql también aplicará las que falten: revisa primero el verify.sql de cada
--    carpeta anterior.
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: [TenantSettings].[RequireOpenCashSession] queda igual al aplicar y al
--    revertir: bit, sin nulos, con el valor por omisión que toman las filas nuevas,
--    (CONVERT([bit],(0))). [Encendida]: negocios que la activaron alguna vez; nada la lee desde
--    que todo cobro exige una caja abierta. Una fase posterior la borra.
SELECT
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[is_nullable] AS [AceptaNulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'RequireOpenCashSession';

IF COL_LENGTH(N'dbo.TenantSettings', N'RequireOpenCashSession') IS NOT NULL
    EXEC(N'SELECT
    COUNT(*) AS [Negocios],
    COUNT(CASE WHEN [RequireOpenCashSession] = 1 THEN 1 END) AS [Encendida]
FROM [TenantSettings];');

-- 3. Después: las dos columnas nuevas de [OrderItems] (decimal(18,3) y decimal(18,2), sin nulos,
--    con 0 por omisión) y la tabla [OrderItemCancellations]. Antes de aplicar y después de
--    rollback.sql: ninguna fila.
SELECT
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.OrderItems') AND c.[name] IN (N'CancelledQuantity', N'CancelledAmount')
ORDER BY c.[name];

SELECT t.[name] AS [Tabla], COUNT(c.[column_id]) AS [Columnas]
FROM sys.tables t
INNER JOIN sys.columns c ON c.[object_id] = t.[object_id]
WHERE t.[name] = N'OrderItemCancellations'
GROUP BY t.[name];

-- 4. Después, y antes de volver a la Fase 14 (con o sin rollback.sql): cuentas de mesa abiertas
--    con platillos cancelados. Deben ser 0 antes de regresar: ciérralas o cancélalas, porque la
--    Fase 14 cobra otra vez lo cancelado al recalcular la cuenta (una ronda nueva).
IF COL_LENGTH(N'dbo.OrderItems', N'CancelledQuantity') IS NOT NULL
    EXEC(N'SELECT
    COUNT(DISTINCT o.[Id]) AS [CuentasAbiertasConCancelados],
    COUNT(*) AS [Renglones]
FROM [Orders] o
INNER JOIN [OrderItems] i ON i.[OrderId] = o.[Id]
WHERE o.[IsTab] = 1
    AND o.[Status] NOT IN (6, 7, 99)
    AND i.[CancelledQuantity] > 0;');

-- 5. Después: renglones con más cancelado que lo pedido o que su importe. Debe ser 0.
IF COL_LENGTH(N'dbo.OrderItems', N'CancelledQuantity') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [RenglonesIncoherentes]
FROM [OrderItems]
WHERE [CancelledQuantity] < 0
    OR [CancelledQuantity] > [Quantity]
    OR [CancelledAmount] < 0
    OR [CancelledAmount] > [TotalPrice];');

-- 6. Después: las dos migraciones quedaron registradas (vacío antes de aplicar y después de
--    rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] IN (N'20261008072542_AddTabItemCancellations', N'20261008074715_StopMappingRequireOpenCashSession')
ORDER BY [MigrationId];
