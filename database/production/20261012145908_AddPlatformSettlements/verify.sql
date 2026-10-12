-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22F-2), la más reciente es
--    20261012112823_AddPlatformPrices. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261012145908_AddPlatformSettlements.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas de [PlatformSettlements], [PlatformSettlementOrders] y
--    [PlatformSettlementAdjustments]: los importes en decimal(18,2), las fechas del periodo y del
--    depósito en date, y nulos solo en [Reference], [Notes], [ClosedAt], [VoidedAt], [VoidReason],
--    [CreatedBy], [UpdatedAt], [UpdatedBy] y [Description]. Antes de aplicar y después de
--    rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] IN (
    OBJECT_ID(N'dbo.PlatformSettlements'),
    OBJECT_ID(N'dbo.PlatformSettlementOrders'),
    OBJECT_ID(N'dbo.PlatformSettlementAdjustments'))
ORDER BY [Tabla], c.[column_id];

-- 3. Después: las restricciones CHECK, seis de [PlatformSettlements] (_Channel, _Closed, _Dates,
--    _Revision, _Status y _Voided) y tres de [PlatformSettlementAdjustments] (_Amount, _Kind y
--    _Other); las llaves FK_PlatformSettlementOrders_PlatformSettlements_SettlementId y
--    FK_PlatformSettlementAdjustments_PlatformSettlements_SettlementId en cascada (CASCADE) y
--    FK_PlatformSettlementOrders_Orders_OrderId sin cascada (NO_ACTION); y los índices: las tres
--    llaves primarias, IX_PlatformSettlements_TenantId_Channel_Open e
--    IX_PlatformSettlementOrders_OrderId_Active ([Unico] 1, con su filtro), e
--    IX_PlatformSettlements_TenantId_PeriodTo, IX_PlatformSettlementOrders_SettlementId,
--    IX_PlatformSettlementOrders_TenantId_OrderId e IX_PlatformSettlementAdjustments_SettlementId.
--    Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT N'CHECK' AS [Tipo], OBJECT_NAME([parent_object_id]) AS [Tabla], [name] AS [Nombre], NULL AS [Unico],
    [definition] COLLATE DATABASE_DEFAULT AS [Definicion]
FROM sys.check_constraints
WHERE [parent_object_id] IN (
    OBJECT_ID(N'dbo.PlatformSettlements'),
    OBJECT_ID(N'dbo.PlatformSettlementOrders'),
    OBJECT_ID(N'dbo.PlatformSettlementAdjustments'))
UNION ALL
SELECT N'FK', OBJECT_NAME([parent_object_id]), [name], NULL, [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [parent_object_id] IN (
    OBJECT_ID(N'dbo.PlatformSettlements'),
    OBJECT_ID(N'dbo.PlatformSettlementOrders'),
    OBJECT_ID(N'dbo.PlatformSettlementAdjustments'))
UNION ALL
SELECT N'INDEX', OBJECT_NAME([object_id]), [name], CAST([is_unique] AS int), [filter_definition] COLLATE DATABASE_DEFAULT
FROM sys.indexes
WHERE [object_id] IN (
    OBJECT_ID(N'dbo.PlatformSettlements'),
    OBJECT_ID(N'dbo.PlatformSettlementOrders'),
    OBJECT_ID(N'dbo.PlatformSettlementAdjustments')) AND [type] > 0
ORDER BY [Tipo], [Tabla], [Nombre];

-- 4. Después de desplegar, y antes de rollback.sql: las liquidaciones guardadas por plataforma
--    ([Canal] 3 Rappi, 4 Uber Eats y 5 DiDi Food) y estado ([Estado] 0 abierta, 1 cerrada, 99
--    anulada), y cuántos negocios las tienen. Con alguna fila, rollback.sql se detiene (error 50010)
--    sin cambiar nada: ninguna bitácora conserva sus pedidos ni sus ajustes. Recién aplicado:
--    ninguna fila.
IF OBJECT_ID(N'[PlatformSettlements]') IS NOT NULL
    EXEC(N'SELECT [Channel] AS [Canal], [Status] AS [Estado], COUNT(*) AS [Liquidaciones], COUNT(DISTINCT [TenantId]) AS [Negocios]
FROM [PlatformSettlements]
GROUP BY [Channel], [Status]
ORDER BY [Canal], [Estado];');

-- 5. Después: consistencia. Cada consulta debe dar 0 filas:
--    a. pedidos o ajustes cuyo negocio no es el de su liquidación, o pedidos de otro negocio;
--    b. pedidos de otra plataforma que la de su liquidación;
--    c. sumas guardadas que no son las de sus pedidos o sus ajustes;
--    d. liquidaciones cerradas que no cuadran;
--    e. pedidos vigentes de una liquidación anulada, o no vigentes de una abierta o cerrada.
IF OBJECT_ID(N'[PlatformSettlements]') IS NOT NULL
BEGIN
    EXEC(N'SELECT N''Pedido'' AS [Fila], so.[Id], so.[TenantId] AS [Negocio], s.[TenantId] AS [NegocioDeLaLiquidacion], o.[TenantId] AS [NegocioDelPedido]
FROM [PlatformSettlementOrders] so
INNER JOIN [PlatformSettlements] s ON s.[Id] = so.[SettlementId]
INNER JOIN [Orders] o ON o.[Id] = so.[OrderId]
WHERE so.[TenantId] <> s.[TenantId] OR o.[TenantId] <> s.[TenantId]
UNION ALL
SELECT N''Ajuste'', sa.[Id], sa.[TenantId], s.[TenantId], NULL
FROM [PlatformSettlementAdjustments] sa
INNER JOIN [PlatformSettlements] s ON s.[Id] = sa.[SettlementId]
WHERE sa.[TenantId] <> s.[TenantId];');
    EXEC(N'SELECT so.[Id] AS [Pedido], s.[Channel] AS [CanalDeLaLiquidacion], d.[Channel] AS [CanalDelPedido]
FROM [PlatformSettlementOrders] so
INNER JOIN [PlatformSettlements] s ON s.[Id] = so.[SettlementId]
LEFT JOIN [PlatformOrderDetails] d ON d.[OrderId] = so.[OrderId]
WHERE d.[Channel] IS NULL OR d.[Channel] <> s.[Channel];');
    EXEC(N'SELECT s.[Id] AS [Liquidacion], s.[OrdersTotal], ISNULL(o.[Suma], 0) AS [SumaDePedidos],
    s.[AdjustmentsTotal], ISNULL(a.[Suma], 0) AS [SumaDeAjustes]
FROM [PlatformSettlements] s
OUTER APPLY (SELECT SUM([ExpectedNet]) AS [Suma] FROM [PlatformSettlementOrders] WHERE [SettlementId] = s.[Id]) o
OUTER APPLY (SELECT SUM([Amount]) AS [Suma] FROM [PlatformSettlementAdjustments] WHERE [SettlementId] = s.[Id]) a
WHERE s.[OrdersTotal] <> ISNULL(o.[Suma], 0) OR s.[AdjustmentsTotal] <> ISNULL(a.[Suma], 0);');
    EXEC(N'SELECT [Id] AS [Liquidacion], [DepositedAmount], [OrdersTotal], [AdjustmentsTotal]
FROM [PlatformSettlements]
WHERE [Status] = 1 AND [DepositedAmount] <> [OrdersTotal] + [AdjustmentsTotal];');
    EXEC(N'SELECT so.[Id] AS [Pedido], s.[Status] AS [Estado], so.[IsActive] AS [Vigente]
FROM [PlatformSettlementOrders] so
INNER JOIN [PlatformSettlements] s ON s.[Id] = so.[SettlementId]
WHERE (s.[Status] = 99 AND so.[IsActive] = 1) OR (s.[Status] <> 99 AND so.[IsActive] = 0);');
END;
