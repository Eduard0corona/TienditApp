-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 27A), la más reciente es
--    20261021002749_AddInventoryCounts. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261021040954_AddInventoryTransfers.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla [InventoryTransfers], sus restricciones [CK_InventoryTransfers_Locations],
--    [CK_InventoryTransfers_Movements], [CK_InventoryTransfers_Lines] y
--    [CK_InventoryTransfers_TotalCost], sus cuatro llaves foráneas y sus índices, entre ellos
--    [IX_InventoryTransfers_TenantId_TransferNumber], [IX_InventoryTransfers_TenantId_IdempotencyKey],
--    [IX_InventoryTransfers_OutMovementId] e [IX_InventoryTransfers_InMovementId] (únicos). Antes de
--    aplicar y después de rollback.sql: ninguna fila.
SELECT
    t.[name] AS [Tabla],
    (SELECT COUNT(*) FROM sys.columns c WHERE c.[object_id] = t.[object_id]) AS [Columnas]
FROM sys.tables t
WHERE t.[name] = N'InventoryTransfers';

SELECT
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[parent_object_id] = OBJECT_ID(N'dbo.InventoryTransfers')
ORDER BY [Restriccion];

SELECT
    fk.[name] AS [LlaveForanea],
    OBJECT_NAME(fk.[referenced_object_id]) AS [Referencia],
    fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] = OBJECT_ID(N'dbo.InventoryTransfers')
ORDER BY [LlaveForanea];

SELECT
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.InventoryTransfers')
    AND i.[name] LIKE N'IX[_]%'
ORDER BY [Indice];

-- 3. Después: cuántas transferencias hay, con cuántos artículos y cuánto valían, y cuántos
--    movimientos son piernas de una transferencia (referencia externa TRF:, dos por transferencia).
--    rollback.sql se detiene con 50031 si hay alguna. Justo después de aplicar: ninguna. Antes de
--    aplicar y después de rollback.sql la tabla no existe y la consulta no corre.
IF OBJECT_ID(N'dbo.InventoryTransfers', N'U') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [InventoryTransfers]) AS [Transferencias],
        (SELECT ISNULL(SUM([LineCount]), 0) FROM [InventoryTransfers]) AS [Articulos],
        (SELECT ISNULL(SUM([TotalCost]), 0) FROM [InventoryTransfers]) AS [Valor],
        (SELECT COUNT(*) FROM [InventoryMovements] WHERE [ExternalReference] LIKE N''TRF:%'') AS [Piernas];');

-- 4. Antes y después de aplicar, igual: los movimientos de inventario de cada tipo (5 entrada y 6
--    salida por transferencia) y las existencias. La migración no cambia ninguno.
SELECT N'Movimientos' AS [Registro], [MovementType] AS [Tipo], COUNT(*) AS [Filas], SUM([TotalAmount]) AS [Monto]
FROM [InventoryMovements]
GROUP BY [MovementType]
UNION ALL
SELECT N'Existencias', NULL, COUNT(*), SUM([OnHandQuantity])
FROM [InventoryStocks]
ORDER BY [Registro], [Tipo];
