-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 26E), la más reciente es
--    20261022153116_AddCreditDueSoon. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261022213253_AddOnlinePaymentReconciliation.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas ([OnlineCheckouts].[Dispute] int sin nulos, 0 por omisión;
--    [OnlineCheckouts].[DisputeUpdatedAt] y [OnlinePaymentAccounts].[PaymentsReviewedUntil]
--    datetimeoffset con nulos), las restricciones y los índices. Antes de aplicar y después de
--    rollback.sql: ninguna columna, la restricción [CK_OnlinePaymentRefunds_MadeInMercadoPago] ni los
--    índices nuevos, y [CK_OnlinePaymentRefunds_Status] dice (0, 1, 2).
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE (c.[object_id] = OBJECT_ID(N'dbo.OnlineCheckouts') AND c.[name] IN (N'Dispute', N'DisputeUpdatedAt'))
    OR (c.[object_id] = OBJECT_ID(N'dbo.OnlinePaymentAccounts') AND c.[name] = N'PaymentsReviewedUntil')
ORDER BY [Tabla], [Columna];

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] IN (
    N'CK_OnlinePaymentRefunds_Status', N'CK_OnlinePaymentRefunds_Link', N'CK_OnlinePaymentRefunds_Settled',
    N'CK_OnlinePaymentRefunds_MadeInMercadoPago', N'CK_OnlineCheckouts_Dispute')
ORDER BY [Tabla], [Restriccion];

SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[name] IN (
    N'IX_OnlinePaymentRefunds_TenantId_ProviderRefundId', N'IX_OnlinePaymentRefunds_TenantId_PaymentId_Chargeback',
    N'IX_OnlineCheckouts_TenantId_Dispute')
ORDER BY [Tabla], [Indice];

-- 3. Antes: devoluciones de un negocio con la misma referencia de Mercado Pago. apply.sql se detiene
--    con 50036 si hay alguna (no debería: Mercado Pago le da a cada devolución su propia referencia).
--    Debe salir sin filas.
SELECT [TenantId], [ProviderRefundId], COUNT(*) AS [Devoluciones]
FROM [OnlinePaymentRefunds]
WHERE [ProviderRefundId] IS NOT NULL
GROUP BY [TenantId], [ProviderRefundId]
HAVING COUNT(*) > 1;

-- 4. Después: lo que ya se registró desde Mercado Pago y las disputas. rollback.sql se detiene con
--    50035 si alguno no es cero. Justo después de aplicar: todos en cero. Antes de aplicar y después
--    de rollback.sql las columnas no existen y la consulta no corre.
IF COL_LENGTH(N'dbo.OnlineCheckouts', N'Dispute') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [OnlinePaymentRefunds] WHERE [Reason] = 4) AS [HechasEnMercadoPago],
        (SELECT COUNT(*) FROM [OnlinePaymentRefunds] WHERE [Reason] = 5) AS [PorReclamacion],
        (SELECT COUNT(*) FROM [OnlinePaymentRefunds] WHERE [Reason] = 6) AS [Contracargos],
        (SELECT COUNT(*) FROM [OnlinePaymentRefunds] WHERE [Status] = 3) AS [RechazadasHechasPorElNegocio],
        (SELECT COUNT(*) FROM [OnlineCheckouts] WHERE [Dispute] = 1) AS [DisputasAbiertas],
        (SELECT COUNT(*) FROM [OnlineCheckouts] WHERE [Dispute] IN (2, 3)) AS [DisputasTerminadas];');

-- 5. Antes y después de aplicar, igual: las devoluciones por motivo y estado, y los cobros por
--    estado. La migración no los cambia.
SELECT [Reason] AS [Motivo], [Status] AS [Estado], COUNT(*) AS [Devoluciones], SUM([Amount]) AS [Monto]
FROM [OnlinePaymentRefunds]
GROUP BY [Reason], [Status]
ORDER BY [Reason], [Status];

SELECT [Status] AS [Estado], COUNT(*) AS [Cobros]
FROM [OnlineCheckouts]
GROUP BY [Status]
ORDER BY [Status];
