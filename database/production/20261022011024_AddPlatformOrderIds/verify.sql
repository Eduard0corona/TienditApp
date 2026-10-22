-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22H), la más reciente es
--    20261021040954_AddInventoryTransfers. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261022011024_AddPlatformOrderIds.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [ExternalOrderId] (nvarchar(64), admite nulos), la restricción
--    [CK_PlatformOrderDetails_ExternalOrderId] y el índice [IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderId]
--    (no único, filtrado). Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] / 2 AS [Largo],
    c.[is_nullable] AS [AdmiteNulos]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.PlatformOrderDetails')
    AND c.[name] = N'ExternalOrderId';

SELECT
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[parent_object_id] = OBJECT_ID(N'dbo.PlatformOrderDetails')
    AND cc.[name] = N'CK_PlatformOrderDetails_ExternalOrderId';

SELECT
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.PlatformOrderDetails')
    AND i.[name] = N'IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderId';

-- 3. Después: cuántos pedidos de cada plataforma tienen su ID largo. rollback.sql se detiene con
--    50032 si hay alguno. Justo después de aplicar: ninguno. Antes de aplicar y después de
--    rollback.sql la columna no existe y la consulta no corre.
IF COL_LENGTH(N'dbo.PlatformOrderDetails', N'ExternalOrderId') IS NOT NULL
    EXEC(N'SELECT [Channel] AS [Plataforma], COUNT(*) AS [ConIdLargo]
        FROM [PlatformOrderDetails]
        WHERE [ExternalOrderId] IS NOT NULL
        GROUP BY [Channel]
        ORDER BY [Channel];');

-- 4. Antes y después de aplicar, igual: los pedidos de cada plataforma y lo que la plataforma debe
--    depositar por ellos. La migración no cambia ninguno.
SELECT [Channel] AS [Plataforma], COUNT(*) AS [Pedidos], SUM([ExpectedSettlement]) AS [NetoEsperado]
FROM [PlatformOrderDetails]
GROUP BY [Channel]
ORDER BY [Channel];
