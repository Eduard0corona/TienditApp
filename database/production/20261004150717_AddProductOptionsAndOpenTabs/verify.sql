-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004142352_AddDecimalQuantitiesAndCfdiData; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: columnas de órdenes. [TableNumber] mide 20 bytes (10 caracteres) antes y
--    100 bytes (50 caracteres) después; [DiningTableId], [IsTab], [OpenTableId] y
--    OrderItems.[Round] solo aparecen después (al revés después de rollback.sql).
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Bytes]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.Orders')
        AND c.[name] IN (N'TableNumber', N'DiningTableId', N'IsTab', N'OpenTableId'))
   OR (c.[object_id] = OBJECT_ID(N'dbo.OrderItems') AND c.[name] = N'Round')
ORDER BY [Tabla], [Columna];

-- 3. Antes y después: tablas nuevas. Antes de aplicar no devuelve filas; después, cinco (vacío
--    otra vez después de rollback.sql).
SELECT t.[name] AS [Tabla]
FROM sys.tables t
WHERE t.[name] IN (N'DiningTables', N'KitchenTickets', N'ProductOptionGroups', N'ProductOptions', N'OrderItemOptions')
ORDER BY t.[name];

-- 4. Antes de rollback.sql: [PedidosConMesaLarga] debe ser 0 (si no, el script se detiene sin
--    cambiar nada, error 50002). Cierra antes las cuentas abiertas; el resto es lo que se perdería.
IF OBJECT_ID(N'dbo.DiningTables') IS NOT NULL
    EXEC(N'SELECT
    (SELECT COUNT(*) FROM [Orders] WHERE LEN([TableNumber]) > 10) AS [PedidosConMesaLarga],
    (SELECT COUNT(*) FROM [Orders] WHERE [OpenTableId] IS NOT NULL) AS [CuentasAbiertas],
    (SELECT COUNT(*) FROM [DiningTables]) AS [Mesas],
    (SELECT COUNT(*) FROM [ProductOptionGroups]) AS [GruposDeOpciones],
    (SELECT COUNT(*) FROM [OrderItemOptions]) AS [OpcionesEnVentas];');

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs';
