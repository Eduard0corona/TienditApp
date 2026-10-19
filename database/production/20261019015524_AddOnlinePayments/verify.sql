-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24H), la más reciente
--    es 20261018141704_AddBusinessReviews. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261019015524_AddOnlinePayments.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las cuatro tablas nuevas con sus índices (entre ellos el único por negocio de las
--    cuentas, el único de pedido de los cobros y los dos únicos de las devoluciones) y sus ocho
--    restricciones. Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] IN (
        OBJECT_ID(N'dbo.OnlinePaymentAccounts'), OBJECT_ID(N'dbo.OnlinePaymentAuthorizations'),
        OBJECT_ID(N'dbo.OnlineCheckouts'), OBJECT_ID(N'dbo.OnlinePaymentRefunds'))
    AND i.[name] IS NOT NULL
ORDER BY [Tabla], i.[name];

-- La columna nueva del reporte de ventas (0 por omisión). Antes de aplicar y después de
-- rollback.sql: ninguna fila.
SELECT [name] AS [Columna], TYPE_NAME([system_type_id]) AS [Tipo], [precision], [scale]
FROM sys.columns
WHERE [object_id] = OBJECT_ID(N'dbo.DailySalesSnapshots') AND [name] = N'OnlineAmount';

SELECT OBJECT_NAME([parent_object_id]) AS [Tabla], [name] AS [Restriccion]
FROM sys.check_constraints
WHERE [parent_object_id] IN (
        OBJECT_ID(N'dbo.OnlinePaymentAccounts'), OBJECT_ID(N'dbo.OnlineCheckouts'), OBJECT_ID(N'dbo.OnlinePaymentRefunds'))
ORDER BY [Tabla], [name];

-- 3. Después: cuántos negocios conectaron su cuenta (y cuántos ofrecen pagar en línea), cuántos
--    pedidos se pagaron o se intentaron pagar en línea y cuántas devoluciones hay (rollback.sql se
--    detiene con 50024 si hay un cobro o una devolución). Justo después de aplicar: ninguno.
--    Antes de aplicar y después de rollback.sql las tablas no existen y la consulta no corre.
IF OBJECT_ID(N'dbo.OnlineCheckouts') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [OnlinePaymentAccounts]) AS [Cuentas],
        (SELECT COUNT(*) FROM [OnlinePaymentAccounts] WHERE [AcceptsPayments] = 1) AS [OfrecenPagar],
        (SELECT COUNT(*) FROM [OnlineCheckouts]) AS [Cobros],
        (SELECT COUNT(*) FROM [OnlineCheckouts] WHERE [Status] = 1) AS [Pagados],
        (SELECT COUNT(*) FROM [OnlinePaymentRefunds]) AS [Devoluciones],
        (SELECT COUNT(*) FROM [OnlinePaymentRefunds] WHERE [Status] = 2) AS [DevolucionesRechazadas];');
