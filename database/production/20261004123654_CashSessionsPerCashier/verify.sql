-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004121539_AddOutboxAndWebhookInbox; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes: negocios con más de una caja abierta. Debe estar vacío: la regla anterior lo
--    impedía, y el índice nuevo (una caja abierta por cajero) no puede chocar.
SELECT [TenantId], COUNT(*) AS [CajasAbiertas]
FROM [CashSessions]
WHERE [Status] = 0
GROUP BY [TenantId]
HAVING COUNT(*) > 1;

-- 3. Antes y después: columnas nuevas. Antes de aplicar no devuelve filas; después, siete
--    (vacío otra vez después de rollback.sql).
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.CashSessions')
        AND c.[name] IN (N'InventoryLocationId', N'OpenedByUserId', N'ExpectedClosingAmount',
                         N'ClosingDifference', N'ClosingNotes', N'CashCountJson'))
   OR (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'RequireOpenCashSession')
ORDER BY [Tabla], [Columna];

-- 4. Después: cómo quedaron las cajas existentes.
--    [SinSucursal]: solo negocios sin ubicaciones activas. [SinCajero]: cajas cuyo correo de
--    apertura no coincide con un miembro del negocio (siguen visibles en Turnos y un admin las
--    puede cerrar). [CerradasSinEsperado] debe ser 0.
IF COL_LENGTH(N'dbo.CashSessions', N'OpenedByUserId') IS NOT NULL
    EXEC(N'SELECT
    COUNT(*) AS [Cajas],
    SUM(CASE WHEN [InventoryLocationId] IS NULL THEN 1 ELSE 0 END) AS [SinSucursal],
    SUM(CASE WHEN [OpenedByUserId] IS NULL THEN 1 ELSE 0 END) AS [SinCajero],
    SUM(CASE WHEN [Status] = 1 AND [ExpectedClosingAmount] IS NULL THEN 1 ELSE 0 END) AS [CerradasSinEsperado],
    SUM(CASE WHEN [Status] = 1 AND [ClosingDifference] <> 0 THEN 1 ELSE 0 END) AS [CerradasConDiferencia]
FROM [CashSessions];');

-- 5. Después: índices de caja. Debe aparecer IX_CashSessions_TenantId_OpenedByUserId_Open y ya no
--    IX_CashSessions_TenantId_SingleOpen (al revés después de rollback.sql).
SELECT [name] AS [Indice]
FROM sys.indexes
WHERE [object_id] = OBJECT_ID(N'dbo.CashSessions')
  AND [name] IN (N'IX_CashSessions_TenantId_OpenedByUserId_Open', N'IX_CashSessions_TenantId_SingleOpen');

-- 6. Antes de rollback.sql: debe estar vacío. Cierra las cajas de más en cada negocio; si no,
--    el índice anterior (una caja abierta por negocio) no se puede crear.
SELECT [TenantId], COUNT(*) AS [CajasAbiertas]
FROM [CashSessions]
WHERE [Status] = 0
GROUP BY [TenantId]
HAVING COUNT(*) > 1;

-- 7. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier';
