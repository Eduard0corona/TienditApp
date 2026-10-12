-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 21), la más reciente es
--    20261011161835_AddSpecialDays. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261012005540_AddDeliveryPlatformOrders.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas de [DeliveryPlatformSettings] y [PlatformOrderDetails] (las tasas en
--    decimal(5,2), los importes en decimal(18,2), [ExternalOrderNumber] nvarchar(30), que son 60
--    bytes en [LongitudMaxima], y todas sin nulos salvo [CreatedBy], [UpdatedAt] y [UpdatedBy]) y
--    [DailySalesSnapshots].[PlatformAmount]: decimal(18,2), sin nulos y con ((0.0)) por omisión.
--    Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [LongitudMaxima],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos],
    d.[definition] AS [PorOmision]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
LEFT JOIN sys.default_constraints d ON d.[object_id] = c.[default_object_id]
WHERE c.[object_id] IN (OBJECT_ID(N'dbo.DeliveryPlatformSettings'), OBJECT_ID(N'dbo.PlatformOrderDetails'))
    OR (c.[object_id] = OBJECT_ID(N'dbo.DailySalesSnapshots') AND c.[name] = N'PlatformAmount')
ORDER BY [Tabla], c.[column_id];

-- 3. Después: las restricciones CHECK, cuatro de [DeliveryPlatformSettings] (_Channel,
--    _CommissionBase, _Percents y _Revision) y siete de [PlatformOrderDetails] (_Amounts,
--    _BusinessCollected, _Channel, _CollectedBy, _CommissionBase, _Number y _Percents); la llave
--    FK_PlatformOrderDetails_Orders_OrderId, que borra en cascada (CASCADE); y los índices: las dos
--    llaves primarias, IX_DeliveryPlatformSettings_TenantId_Channel e IX_PlatformOrderDetails_OrderId
--    ([Unico] 1), IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderNumber_RegisteredAt e
--    IX_PlatformOrderDetails_TenantId_RegisteredAt. Ninguna compara importes entre sí: solo contra 0.
--    Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT N'CHECK' AS [Tipo], OBJECT_NAME([parent_object_id]) AS [Tabla], [name] AS [Nombre], NULL AS [Unico],
    [definition] COLLATE DATABASE_DEFAULT AS [Definicion]
FROM sys.check_constraints
WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.DeliveryPlatformSettings'), OBJECT_ID(N'dbo.PlatformOrderDetails'))
UNION ALL
SELECT N'FK', OBJECT_NAME([parent_object_id]), [name], NULL, [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [parent_object_id] = OBJECT_ID(N'dbo.PlatformOrderDetails')
UNION ALL
SELECT N'INDEX', OBJECT_NAME([object_id]), [name], CAST([is_unique] AS int), NULL
FROM sys.indexes
WHERE [object_id] IN (OBJECT_ID(N'dbo.DeliveryPlatformSettings'), OBJECT_ID(N'dbo.PlatformOrderDetails')) AND [type] > 0
ORDER BY [Tipo], [Tabla], [Nombre];

-- 4. Antes y después: pedidos por canal (0 Mostrador, 1 WhatsApp, 2 En línea, 3 Rappi, 4 Uber Eats
--    y 5 DiDi Food) y pagos y reembolsos con el método 5 («Plataforma»). Antes de aplicar, y hasta
--    desplegar, no hay canales 3 a 5 y el método 5 da 0 filas.
SELECT [Channel] AS [Canal], COUNT(*) AS [Pedidos], COUNT(DISTINCT [TenantId]) AS [Negocios]
FROM [Orders]
GROUP BY [Channel]
ORDER BY [Channel];

SELECT N'Pagos' AS [Tipo], COUNT(*) AS [Filas], COALESCE(SUM([Amount]), 0) AS [Importe]
FROM [OrderPayments]
WHERE [Method] = 5
UNION ALL
SELECT N'Reembolsos', COUNT(*), COALESCE(SUM([Amount]), 0)
FROM [OrderRefunds]
WHERE [Method] = 5;

-- 5. Antes de rollback.sql: lo que lo detiene (error 50008) y lo que se perdería. Si una de las
--    cuatro primeras filas tiene [Filas] mayor que 0, rollback.sql no cambia nada: solo queda
--    corregir hacia adelante. Los movimientos de caja con el método 5 siempre deben ser 0, porque lo
--    que cobra una plataforma no entra a ninguna caja. Debajo, las configuraciones por plataforma
--    ([Canal]) y estado ([Activa]): rollback.sql las borra (la bitácora DeliveryPlatformUpdated
--    conserva cada valor).
SELECT N'Pedidos de plataforma (canal 3 a 5)' AS [Concepto], COUNT(*) AS [Filas]
FROM [Orders]
WHERE [Channel] IN (3, 4, 5)
UNION ALL
SELECT N'Pagos «Plataforma» (método 5)', COUNT(*)
FROM [OrderPayments]
WHERE [Method] = 5
UNION ALL
SELECT N'Reembolsos «Plataforma» (método 5)', COUNT(*)
FROM [OrderRefunds]
WHERE [Method] = 5
UNION ALL
SELECT N'Movimientos de caja con el método 5 (siempre 0)', COUNT(*)
FROM [CashTransactions]
WHERE [PaymentMethod] = 5;

IF OBJECT_ID(N'[DeliveryPlatformSettings]') IS NOT NULL
    EXEC(N'SELECT [Channel] AS [Canal], [IsActive] AS [Activa], COUNT(*) AS [Negocios]
FROM [DeliveryPlatformSettings]
GROUP BY [Channel], [IsActive]
ORDER BY [Channel], [IsActive];');

-- 6. Después: consistencia. Las dos consultas deben dar 0 filas: pedidos de plataforma sin su
--    detalle, y detalles que cobró la plataforma ([CollectedBy] 0) sin un pago «Plataforma» por el
--    total con el que se registraron.
IF OBJECT_ID(N'[PlatformOrderDetails]') IS NOT NULL
BEGIN
    EXEC(N'SELECT o.[TenantId] AS [Negocio], o.[Id] AS [Pedido], o.[OrderNumber] AS [Folio], o.[Channel] AS [Canal]
FROM [Orders] o
WHERE o.[Channel] IN (3, 4, 5)
    AND NOT EXISTS (SELECT 1 FROM [PlatformOrderDetails] d WHERE d.[OrderId] = o.[Id]);');
    EXEC(N'SELECT d.[TenantId] AS [Negocio], d.[OrderId] AS [Pedido], d.[ExternalOrderNumber] AS [FolioDeLaApp], d.[OrderTotal] AS [Total]
FROM [PlatformOrderDetails] d
WHERE d.[CollectedBy] = 0
    AND NOT EXISTS (
        SELECT 1 FROM [OrderPayments] p
        WHERE p.[OrderId] = d.[OrderId] AND p.[Method] = 5 AND p.[Amount] = d.[OrderTotal]);');
END;

-- 7. Después de desplegar: días que guardó la versión anterior durante un swap que cruzó la
--    medianoche, con lo que cobraron las plataformas en [OtherAmount]. Son los de negocios con pagos
--    «Plataforma» con [PlatformAmount] 0, [OtherAmount] mayor que 0 y [ComputedAt] posterior al
--    primer pago «Plataforma» del negocio. También lista días bien calculados con pagos del método
--    «Otro» y sin plataformas: no es un error. Para separar los que guardó la versión anterior y
--    repararlos, usa database/production/data-repair/20261012_PlatformAmountSnapshots con la hora
--    en que terminó el swap (DesplegadoEn); su repair.sql borra esos días y el worker los vuelve a
--    calcular.
IF COL_LENGTH(N'dbo.DailySalesSnapshots', N'PlatformAmount') IS NOT NULL
    EXEC(N'SELECT s.[TenantId] AS [Negocio], s.[BusinessDate] AS [Dia], s.[BranchId] AS [Sucursal], s.[OrderType] AS [TipoDePedido],
    s.[OtherAmount] AS [Otros], s.[ComputedAt] AS [Calculado]
FROM [DailySalesSnapshots] s
INNER JOIN (
    SELECT [TenantId], MIN([PaidAt]) AS [PrimerPagoPlataforma]
    FROM [OrderPayments]
    WHERE [Method] = 5
    GROUP BY [TenantId]
) f ON f.[TenantId] = s.[TenantId]
WHERE s.[PlatformAmount] = 0 AND s.[OtherAmount] > 0 AND s.[ComputedAt] > f.[PrimerPagoPlataforma]
ORDER BY s.[TenantId], s.[BusinessDate];');
