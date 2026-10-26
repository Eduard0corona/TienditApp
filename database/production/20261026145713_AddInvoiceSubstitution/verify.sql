-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas; nunca
-- muestran datos fiscales de los clientes.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28F), la más reciente es
--    20261026082041_AddSelfInvoicing. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261026145713_AddInvoiceSubstitution.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [OrderInvoices].[ReplacedByInvoiceId] (uniqueidentifier, nula, sin llave
--    foránea), los índices [IX_OrderInvoices_ReplacedByInvoiceId] y
--    [IX_OrderInvoices_TenantId_OrderId_Live] (su filtro deja fuera a las sustituidas) y las
--    restricciones [CK_OrderInvoices_CancellationMotive] (con el motivo 01) y
--    [CK_OrderInvoices_Replacement]. Antes de aplicar y después de rollback.sql: sin la columna, el
--    índice nuevo ni [CK_OrderInvoices_Replacement]; el índice de las vivas filtra solo por [Status] y
--    la restricción del motivo acepta 2 y 3.
SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.OrderInvoices') AND c.[name] = N'ReplacedByInvoiceId';

SELECT
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.OrderInvoices')
    AND i.[name] IN (N'IX_OrderInvoices_ReplacedByInvoiceId', N'IX_OrderInvoices_TenantId_OrderId_Live')
ORDER BY [Indice];

SELECT
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] IN (N'CK_OrderInvoices_CancellationMotive', N'CK_OrderInvoices_Replacement')
ORDER BY [Restriccion];

-- 3. Después: las facturas sustituidas (por estado: 1 timbrada, 3 cancelándose, 4 cancelada) y las
--    canceladas con el motivo 01. rollback.sql se detiene con 50043 si hay alguna sustituida. Justo
--    después de aplicar: ninguna. Antes de aplicar y después de rollback.sql la columna no existe y la
--    consulta no corre.
IF COL_LENGTH(N'dbo.OrderInvoices', N'ReplacedByInvoiceId') IS NOT NULL
    EXEC(N'SELECT [Status] AS [Estado], COUNT(*) AS [Sustituidas],
            SUM(CASE WHEN [CancellationMotive] = 1 THEN 1 ELSE 0 END) AS [ConMotivo01]
        FROM [OrderInvoices]
        WHERE [ReplacedByInvoiceId] IS NOT NULL
        GROUP BY [Status]
        ORDER BY [Status];');

-- 4. Antes y después: las facturas de pedidos por estado (0 timbrando, 1 timbrada, 2 rechazada,
--    3 cancelándose, 4 cancelada) y los pedidos con más de una viva, que antes de aplicar son cero y
--    después solo pueden ser los de una factura sustituida que el SAT todavía no cancela. La migración
--    no cambia ninguna.
IF OBJECT_ID(N'dbo.OrderInvoices') IS NOT NULL
    EXEC(N'SELECT [Status] AS [Estado], COUNT(*) AS [Facturas]
        FROM [OrderInvoices]
        GROUP BY [Status]
        ORDER BY [Status];
    SELECT COUNT(*) AS [PedidosConVariasVivas]
    FROM (
        SELECT [TenantId], [OrderId]
        FROM [OrderInvoices]
        WHERE [Status] IN (0, 1, 3)
        GROUP BY [TenantId], [OrderId]
        HAVING COUNT(*) > 1
    ) AS v;');
