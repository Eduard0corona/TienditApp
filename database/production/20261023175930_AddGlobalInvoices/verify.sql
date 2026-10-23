-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28A), la más reciente es
--    20261023064133_AddOrderInvoicing. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261023175930_AddGlobalInvoices.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las tablas nuevas [GlobalInvoices] y [GlobalInvoiceOrders], sus restricciones y sus
--    índices. Antes de aplicar y después de rollback.sql: nada de esto.
SELECT [name] AS [Tabla]
FROM sys.tables
WHERE [name] IN (N'GlobalInvoices', N'GlobalInvoiceOrders')
ORDER BY [name];

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion]
FROM sys.check_constraints cc
WHERE cc.[name] LIKE N'CK[_]GlobalInvoices[_]%' OR cc.[name] LIKE N'CK[_]GlobalInvoiceOrders[_]%'
ORDER BY [Tabla], [Restriccion];

SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] IN (OBJECT_ID(N'dbo.GlobalInvoices'), OBJECT_ID(N'dbo.GlobalInvoiceOrders')) AND i.[name] IS NOT NULL
ORDER BY [Tabla], [Indice];

-- 3. Después: las facturas globales por estado y las ventas que tienen tomadas (de las vivas).
--    rollback.sql se detiene con 50038 si hay alguna factura global. Justo después de aplicar: todo en
--    cero. Antes de aplicar y después de rollback.sql las tablas no existen y la consulta no corre.
IF OBJECT_ID(N'dbo.GlobalInvoices') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [GlobalInvoices] WHERE [Status] = 0) AS [Timbrando],
        (SELECT COUNT(*) FROM [GlobalInvoices] WHERE [Status] = 1) AS [Timbradas],
        (SELECT COUNT(*) FROM [GlobalInvoices] WHERE [Status] = 2) AS [Rechazadas],
        (SELECT COUNT(*) FROM [GlobalInvoices] WHERE [Status] = 3) AS [Cancelandose],
        (SELECT COUNT(*) FROM [GlobalInvoices] WHERE [Status] = 4) AS [Canceladas],
        (SELECT COUNT(*) FROM [GlobalInvoiceOrders] WHERE [Released] = 0) AS [VentasTomadas];');

-- 4. Antes y después de aplicar, igual: las facturas de pedidos por estado (la migración no las cambia).
IF OBJECT_ID(N'dbo.OrderInvoices') IS NOT NULL
    EXEC(N'SELECT [Status], COUNT(*) AS [Facturas] FROM [OrderInvoices] GROUP BY [Status] ORDER BY [Status];');
