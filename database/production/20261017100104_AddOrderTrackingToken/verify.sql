-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24B), la más reciente es
--    20261017070332_AddAccountAddresses. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261017100104_AddOrderTrackingToken.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [Orders].[TrackingToken] en varchar(43) con nulos, la restricción
--    CK_Orders_TrackingToken y el índice único filtrado IX_Orders_TrackingToken ([Unico] 1, con el
--    filtro ([TrackingToken] IS NOT NULL)). SQL Server muestra la CHECK a su manera (por ejemplo,
--    ([TrackingToken] IS NULL OR len([TrackingToken])=(43))). Antes de aplicar y después de
--    rollback.sql: ninguna fila.
SELECT N'COLUMN' AS [Tipo], c.[name] AS [Nombre], CONCAT(t.[name], N'(', c.[max_length], N')') AS [Definicion],
    CAST(c.[is_nullable] AS int) AS [AceptaNulosOUnico]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.Orders') AND c.[name] = N'TrackingToken'
UNION ALL
SELECT N'CHECK', [name], [definition] COLLATE DATABASE_DEFAULT, NULL
FROM sys.check_constraints
WHERE [parent_object_id] = OBJECT_ID(N'dbo.Orders') AND [name] = N'CK_Orders_TrackingToken'
UNION ALL
SELECT N'INDEX', [name], [filter_definition] COLLATE DATABASE_DEFAULT, CAST([is_unique] AS int)
FROM sys.indexes
WHERE [object_id] = OBJECT_ID(N'dbo.Orders') AND [name] = N'IX_Orders_TrackingToken'
ORDER BY [Tipo];

-- 3. Después de desplegar, y antes de rollback.sql: cuántos pedidos tienen enlace de seguimiento y
--    de cuántos negocios (solo los del menú en línea hechos desde el despliegue). Recién aplicado: 0.
IF COL_LENGTH(N'dbo.Orders', N'TrackingToken') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [PedidosConEnlace], COUNT(DISTINCT [TenantId]) AS [Negocios]
FROM [Orders]
WHERE [TrackingToken] IS NOT NULL;');

-- 4. Después: consistencia. Cada consulta debe dar 0 filas:
--    a. enlaces que no tienen la forma de uno (43 caracteres de Base64 para URL: letras, dígitos,
--       «-» y «_»);
--    b. enlaces en pedidos que no son del menú en línea ([Channel] 2 es Online).
IF COL_LENGTH(N'dbo.Orders', N'TrackingToken') IS NOT NULL
BEGIN
    EXEC(N'SELECT [Id], [TenantId], [OrderNumber]
FROM [Orders]
WHERE [TrackingToken] IS NOT NULL
    AND (LEN([TrackingToken]) <> 43 OR [TrackingToken] COLLATE Latin1_General_BIN LIKE ''%[^A-Za-z0-9_-]%'');');
    EXEC(N'SELECT [Id], [TenantId], [OrderNumber], [Channel]
FROM [Orders]
WHERE [TrackingToken] IS NOT NULL AND [Channel] <> 2;');
END;
