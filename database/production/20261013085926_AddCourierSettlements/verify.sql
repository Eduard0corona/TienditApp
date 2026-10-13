-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22C), la más reciente es
--    20261013042014_AddOrderDeliveries. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261013085926_AddCourierSettlements.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas de [OrderDeliveries] ([PaysWith] y [CashToCollect] en
--    decimal(18,2), [DispatchedAt] datetimeoffset y [DispatchedBy] nvarchar(256), las cuatro con
--    nulos) y las de las tres tablas nuevas, con nulos solo en [Reference], [Notes], [SettledBy],
--    [CreatedBy], [UpdatedAt], [UpdatedBy] y [Description]. [Largo] va en bytes (nvarchar(60) es
--    120). Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [Largo],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE (c.[object_id] = OBJECT_ID(N'dbo.OrderDeliveries') AND c.[name] IN (N'PaysWith', N'DispatchedAt', N'DispatchedBy', N'CashToCollect'))
    OR c.[object_id] IN (OBJECT_ID(N'dbo.CourierSettlements'), OBJECT_ID(N'dbo.CourierSettlementOrders'), OBJECT_ID(N'dbo.CourierSettlementAdjustments'))
ORDER BY [Tabla], c.[column_id];

-- 3. Después: las restricciones CHECK nuevas (CK_OrderDeliveries_PaysWith, _Dispatch y
--    _CashToCollect; CK_CourierSettlements_Balances, _Courier, _HandedAmount y _Method;
--    CK_CourierSettlementOrders_Amount; CK_CourierSettlementAdjustments_Amount, _Kind y _Other),
--    las llaves (a [CashSessions] y a [Orders] sin cascada, NO_ACTION; a la liquidación en cascada,
--    CASCADE) y los índices, entre ellos IX_CourierSettlementOrders_OrderId ([Unico] 1). SQL Server
--    muestra las CHECK a su manera (por ejemplo, ([HandedAmount]>=(0))). Antes de aplicar y después
--    de rollback.sql: ninguna fila.
SELECT OBJECT_NAME([parent_object_id]) AS [Tabla], N'CHECK' AS [Tipo], [name] AS [Nombre], NULL AS [Unico],
    [definition] COLLATE DATABASE_DEFAULT AS [Definicion]
FROM sys.check_constraints
WHERE [name] IN (N'CK_OrderDeliveries_PaysWith', N'CK_OrderDeliveries_Dispatch', N'CK_OrderDeliveries_CashToCollect')
    OR [parent_object_id] IN (OBJECT_ID(N'dbo.CourierSettlements'), OBJECT_ID(N'dbo.CourierSettlementOrders'), OBJECT_ID(N'dbo.CourierSettlementAdjustments'))
UNION ALL
SELECT OBJECT_NAME([parent_object_id]), N'FK', [name], NULL, [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.CourierSettlements'), OBJECT_ID(N'dbo.CourierSettlementOrders'), OBJECT_ID(N'dbo.CourierSettlementAdjustments'))
UNION ALL
SELECT OBJECT_NAME([object_id]), N'INDEX', [name], CAST([is_unique] AS int), [filter_definition] COLLATE DATABASE_DEFAULT
FROM sys.indexes
WHERE [object_id] IN (OBJECT_ID(N'dbo.CourierSettlements'), OBJECT_ID(N'dbo.CourierSettlementOrders'), OBJECT_ID(N'dbo.CourierSettlementAdjustments'))
    AND [type] > 0
ORDER BY [Tabla], [Tipo], [Nombre];

-- 4. Después de desplegar, y antes de volver a la versión anterior o de rollback.sql:
--    a. los pedidos a domicilio que salieron con el repartidor o dijeron con cuánto pagan, por
--       estado ([Estado]: 0 pendiente, 1 confirmado, 2 en preparación, 3 listo, 4 aceptado,
--       5 en proceso, 6 entregado, 7 completado, 99 cancelado; de 1 a 5 van en camino): cuántos
--       cobra el repartidor, cuánto cobra, cuántos debe todavía (entregados, con saldo y sin
--       liquidar) y cuánto;
--    b. las liquidaciones de repartidor por negocio: cuántas, cuánto entregaron y cuánto sumaron sus
--       ajustes.
--    Con alguna fila en cualquiera de las dos, rollback.sql se detiene (error 50014) sin cambiar nada.
--    Volver la app atrás con pedidos en camino o debidos: esa versión no lo sabe, y el efectivo
--    pendiente se cobra a mano desde Pedidos. Recién aplicado: ninguna fila.
IF COL_LENGTH(N'dbo.OrderDeliveries', N'DispatchedAt') IS NOT NULL
    EXEC(N'SELECT o.[Status] AS [Estado], COUNT(*) AS [Pedidos],
    SUM(CASE WHEN d.[CashToCollect] IS NULL THEN 0 ELSE 1 END) AS [CobraElRepartidor], SUM(d.[CashToCollect]) AS [Cobra],
    SUM(CASE WHEN o.[Status] = 6 AND d.[CashToCollect] IS NOT NULL AND o.[Total] > o.[PaidAmount] AND s.[OrderId] IS NULL
        THEN 1 ELSE 0 END) AS [Debidos],
    SUM(CASE WHEN o.[Status] = 6 AND d.[CashToCollect] IS NOT NULL AND o.[Total] > o.[PaidAmount] AND s.[OrderId] IS NULL
        THEN o.[Total] - o.[PaidAmount] ELSE 0 END) AS [Debe]
FROM [OrderDeliveries] d
INNER JOIN [Orders] o ON o.[Id] = d.[OrderId]
LEFT JOIN [CourierSettlementOrders] s ON s.[OrderId] = o.[Id]
WHERE d.[DispatchedAt] IS NOT NULL OR d.[PaysWith] IS NOT NULL
GROUP BY o.[Status]
ORDER BY [Estado];');
IF OBJECT_ID(N'dbo.CourierSettlements') IS NOT NULL
    EXEC(N'SELECT [TenantId] AS [Negocio], COUNT(*) AS [Liquidaciones], SUM([HandedAmount]) AS [Entregado],
    SUM([AdjustmentsTotal]) AS [Ajustes], MAX([SettledAt]) AS [Ultima]
FROM [CourierSettlements]
GROUP BY [TenantId]
ORDER BY [Liquidaciones] DESC;');

-- 5. Después: consistencia. Cada consulta debe dar 0 filas:
--    a. pedidos o ajustes de una liquidación de otro negocio, o pedidos de otro negocio;
--    b. liquidaciones que entraron a una caja de otro negocio;
--    c. liquidaciones cuyos pedidos o ajustes no suman lo que guardan;
--    d. pedidos liquidados que no salieron para que el repartidor los cobrara.
IF OBJECT_ID(N'dbo.CourierSettlements') IS NOT NULL
BEGIN
    EXEC(N'SELECT l.[Id], l.[TenantId] AS [NegocioDelRenglon], s.[TenantId] AS [NegocioDeLaLiquidacion], o.[TenantId] AS [NegocioDelPedido]
FROM [CourierSettlementOrders] l
INNER JOIN [CourierSettlements] s ON s.[Id] = l.[SettlementId]
INNER JOIN [Orders] o ON o.[Id] = l.[OrderId]
WHERE l.[TenantId] <> s.[TenantId] OR o.[TenantId] <> s.[TenantId]
UNION ALL
SELECT a.[Id], a.[TenantId], s.[TenantId], NULL
FROM [CourierSettlementAdjustments] a
INNER JOIN [CourierSettlements] s ON s.[Id] = a.[SettlementId]
WHERE a.[TenantId] <> s.[TenantId];');
    EXEC(N'SELECT s.[Id], s.[TenantId] AS [NegocioDeLaLiquidacion], c.[TenantId] AS [NegocioDeLaCaja]
FROM [CourierSettlements] s
INNER JOIN [CashSessions] c ON c.[Id] = s.[CashSessionId]
WHERE c.[TenantId] <> s.[TenantId];');
    EXEC(N'SELECT s.[Id], s.[OrdersTotal], s.[AdjustmentsTotal],
    (SELECT SUM(l.[Amount]) FROM [CourierSettlementOrders] l WHERE l.[SettlementId] = s.[Id]) AS [SumaPedidos],
    (SELECT SUM(a.[Amount]) FROM [CourierSettlementAdjustments] a WHERE a.[SettlementId] = s.[Id]) AS [SumaAjustes]
FROM [CourierSettlements] s
WHERE s.[OrdersTotal] <> ISNULL((SELECT SUM(l.[Amount]) FROM [CourierSettlementOrders] l WHERE l.[SettlementId] = s.[Id]), 0)
    OR s.[AdjustmentsTotal] <> ISNULL((SELECT SUM(a.[Amount]) FROM [CourierSettlementAdjustments] a WHERE a.[SettlementId] = s.[Id]), 0);');
    EXEC(N'SELECT l.[OrderId], l.[SettlementId]
FROM [CourierSettlementOrders] l
LEFT JOIN [OrderDeliveries] d ON d.[OrderId] = l.[OrderId]
WHERE d.[Id] IS NULL OR d.[CashToCollect] IS NULL;');
END;
