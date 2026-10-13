-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22B), la más reciente es
--    20261013002425_AddDeliveryZones. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261013042014_AddOrderDeliveries.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas de [OrderDeliveries]: el código postal y el folio en varchar, el envío
--    en decimal(18,2), la tasa en decimal(5,2), y nulos solo en [References], [Courier],
--    [CourierFolio], [TrackingUrl], [ShipmentRegisteredAt] y [ShipmentRegisteredBy]. [Largo] va en
--    bytes (nvarchar(300) es 600). Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [Largo],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.OrderDeliveries')
ORDER BY c.[column_id];

-- 3. Después: las seis restricciones CHECK (CK_OrderDeliveries_AddressLine, _Fee, _FeeTaxRate,
--    _PostalCode, _Shipment y _TrackingUrl), la llave FK_OrderDeliveries_Orders_OrderId en cascada
--    (CASCADE) y los índices: la llave primaria e IX_OrderDeliveries_OrderId ([Unico] 1). SQL Server
--    muestra las CHECK a su manera (por ejemplo, ([Fee]>=(0) AND [Fee]<=(10000))). Antes de aplicar y
--    después de rollback.sql: ninguna fila.
SELECT N'CHECK' AS [Tipo], [name] AS [Nombre], NULL AS [Unico], [definition] COLLATE DATABASE_DEFAULT AS [Definicion]
FROM sys.check_constraints
WHERE [parent_object_id] = OBJECT_ID(N'dbo.OrderDeliveries')
UNION ALL
SELECT N'FK', [name], NULL, [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [parent_object_id] = OBJECT_ID(N'dbo.OrderDeliveries')
UNION ALL
SELECT N'INDEX', [name], CAST([is_unique] AS int), [filter_definition] COLLATE DATABASE_DEFAULT
FROM sys.indexes
WHERE [object_id] = OBJECT_ID(N'dbo.OrderDeliveries') AND [type] > 0
ORDER BY [Tipo], [Nombre];

-- 4. Después de desplegar, y antes de volver a la versión anterior o de rollback.sql: los pedidos
--    a domicilio por estado ([Estado]: 0 pendiente, 1 confirmado, 2 en preparación, 3 listo,
--    6 entregado, 7 completado, 99 cancelado), cuántos tienen envío de Paquetenvia, cuánto suman
--    sus envíos y cuántos negocios los tienen. Con alguna fila, rollback.sql se detiene (error
--    50013) sin cambiar nada. Volver la app atrás con pedidos abiertos (estados 0 a 3): esa versión
--    no conoce el envío, aunque su total ya lo incluye. Recién aplicado: ninguna fila.
IF OBJECT_ID(N'dbo.OrderDeliveries') IS NOT NULL
    EXEC(N'SELECT o.[Status] AS [Estado], COUNT(*) AS [Pedidos], SUM(CASE WHEN d.[Courier] IS NULL THEN 0 ELSE 1 END) AS [ConPaquetenvia],
    SUM(d.[Fee]) AS [Envios], COUNT(DISTINCT d.[TenantId]) AS [Negocios]
FROM [OrderDeliveries] d
INNER JOIN [Orders] o ON o.[Id] = d.[OrderId]
GROUP BY o.[Status]
ORDER BY [Estado];');

-- 5. Después: consistencia. Cada consulta debe dar 0 filas:
--    a. entregas cuyo negocio no es el de su pedido;
--    b. entregas de pedidos que no se mandan a domicilio: de Rappi, Uber Eats o DiDi (canales 3 a
--       5), cuentas de mesa o servicios.
IF OBJECT_ID(N'dbo.OrderDeliveries') IS NOT NULL
BEGIN
    EXEC(N'SELECT d.[Id], d.[TenantId] AS [NegocioDeLaEntrega], o.[TenantId] AS [NegocioDelPedido]
FROM [OrderDeliveries] d
INNER JOIN [Orders] o ON o.[Id] = d.[OrderId]
WHERE d.[TenantId] <> o.[TenantId];');
    EXEC(N'SELECT d.[Id], o.[Channel] AS [Canal], o.[IsTab] AS [Cuenta], o.[OrderType] AS [Tipo]
FROM [OrderDeliveries] d
INNER JOIN [Orders] o ON o.[Id] = d.[OrderId]
WHERE o.[Channel] IN (3, 4, 5) OR o.[IsTab] = 1 OR o.[OrderType] <> 0;');
END;

-- 6. Antes del merge: negocios con zonas de entrega activas cuyos precios no incluyen IVA. Desde esta
--    fase el pedido les cobra el envío como sus precios, con el IVA sumado: una zona de $35.00 cobra
--    $40.60 con IVA de 16 % (ADR 0018). La pantalla de la Fase 22B les decía que el monto era con IVA
--    incluido: avísales, o ajusta el envío de sus zonas. Lo común es ninguna fila (los precios incluyen
--    IVA por omisión).
IF OBJECT_ID(N'dbo.DeliveryZones') IS NOT NULL
    EXEC(N'SELECT s.[TenantId] AS [Negocio], s.[DefaultTaxRate] AS [Tasa], COUNT(*) AS [ZonasActivas],
       MIN(z.[Fee]) AS [EnvioMenor], MAX(z.[Fee]) AS [EnvioMayor]
FROM [DeliveryZones] z
INNER JOIN [TenantSettings] s ON s.[TenantId] = z.[TenantId]
WHERE z.[IsActive] = 1 AND s.[PricesIncludeTax] = 0
GROUP BY s.[TenantId], s.[DefaultTaxRate]
ORDER BY [ZonasActivas] DESC;');
