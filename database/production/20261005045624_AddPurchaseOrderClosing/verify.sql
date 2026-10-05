-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261005022701_AddProductStockItems; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: las columnas nuevas. Antes de aplicar no devuelve filas; después,
--    [CloseReason] (nvarchar(500)) y [ClosedAt] (datetimeoffset), las dos con nulos (vacío otra
--    vez después de rollback.sql).
SELECT c.[name] AS [Columna], t.[name] AS [Tipo], c.[max_length] AS [Bytes], c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.PurchaseOrders') AND c.[name] IN (N'ClosedAt', N'CloseReason')
ORDER BY c.[name];

-- 3. Antes de rollback.sql: órdenes cerradas sin el resto (estado 4), por negocio. Si hay alguna,
--    rollback.sql se detiene con el error 50007 sin cambiar nada. Justo después de aplicar no hay
--    ninguna: solo se cierran desde la aplicación.
SELECT t.[Name] AS [Negocio], COUNT(*) AS [ComprasCerradasSinElResto]
FROM [PurchaseOrders] po
INNER JOIN [Tenants] t ON t.[Id] = po.[TenantId]
WHERE po.[Status] = 4
GROUP BY t.[Name]
ORDER BY [ComprasCerradasSinElResto] DESC;

-- 4. Antes y después: órdenes de compra por estado (0 borrador, 1 enviada, 2 recibida en parte,
--    3 recibida, 4 cerrada sin el resto, 99 cancelada). Aplicar no cambia ninguna.
SELECT po.[Status] AS [Estado], COUNT(*) AS [Ordenes]
FROM [PurchaseOrders] po
GROUP BY po.[Status]
ORDER BY po.[Status];

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261005045624_AddPurchaseOrderClosing';
