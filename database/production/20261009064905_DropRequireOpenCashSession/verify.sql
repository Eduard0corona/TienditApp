-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261009041644_StopMappingRequireOpenCashSession (PR #47). Si es anterior, no apliques
--    apply.sql mientras corra esa versión, que lee la columna: despliega antes el PR #47.
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes: lo que depende de la columna. Debe salir solo la restricción de su valor por
--    omisión, que apply.sql borra, y ningún índice, estadística ni objeto. Hay que resolver antes
--    de aplicar: un índice; una estadística con [Automatica] = 0 (creada a mano: bloquea el
--    borrado, se quita con DROP STATISTICS); o una vista, función o procedimiento que use la
--    tabla. Las estadísticas automáticas se borran solas con la columna.
--    Después de aplicar: ninguna fila.
SELECT OBJECT_NAME(dc.[parent_object_id]) AS [Tabla], c.[name] AS [Columna], dc.[name] AS [PorOmision]
FROM sys.default_constraints dc
INNER JOIN sys.columns c ON c.[object_id] = dc.[parent_object_id] AND c.[column_id] = dc.[parent_column_id]
WHERE dc.[parent_object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'RequireOpenCashSession';

SELECT OBJECT_NAME(ic.[object_id]) AS [Tabla], i.[name] AS [Indice], c.[name] AS [Columna]
FROM sys.index_columns ic
INNER JOIN sys.indexes i ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id]
INNER JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
WHERE ic.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'RequireOpenCashSession';

SELECT OBJECT_NAME(s.[object_id]) AS [Tabla], s.[name] AS [Estadistica], c.[name] AS [Columna], s.[auto_created] AS [Automatica]
FROM sys.stats s
INNER JOIN sys.stats_columns sc ON sc.[object_id] = s.[object_id] AND sc.[stats_id] = s.[stats_id]
INNER JOIN sys.columns c ON c.[object_id] = sc.[object_id] AND c.[column_id] = sc.[column_id]
WHERE s.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'RequireOpenCashSession'
    AND NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.[object_id] = s.[object_id] AND i.[index_id] = s.[stats_id]);

-- (La tabla misma no cuenta: sus restricciones la referencian.)
SELECT OBJECT_SCHEMA_NAME(d.[referencing_id]) + N'.' + OBJECT_NAME(d.[referencing_id]) AS [ObjetoQueUsaLaTabla],
    OBJECT_NAME(d.[referenced_id]) AS [Tabla]
FROM sys.sql_expression_dependencies d
WHERE d.[referenced_id] = OBJECT_ID(N'dbo.TenantSettings')
    AND d.[referencing_id] <> OBJECT_ID(N'dbo.TenantSettings')
    AND OBJECTPROPERTY(d.[referencing_id], N'IsTable') = 0;

-- 3. Antes: cuántos negocios tenían activada la política, lo que se pierde al aplicar (cuáles,
--    solo queda en el respaldo). [ConPoliticaActivada] debe coincidir con la consulta 3 del
--    verify.sql del PR #47: desde el PR #40 nada la cambia, y su valor ya no cambia nada (todo
--    cobro y reembolso exige caja). Después de aplicar no sale: la columna ya no existe. Después
--    de rollback.sql: [ConPoliticaActivada] en 0.
IF COL_LENGTH(N'dbo.TenantSettings', N'RequireOpenCashSession') IS NOT NULL
    EXEC(N'SELECT
    COUNT(*) AS [Negocios],
    COUNT(CASE WHEN [RequireOpenCashSession] = 1 THEN 1 END) AS [ConPoliticaActivada]
FROM [TenantSettings];');

-- 4. Antes y después: la columna. Antes: [RequireOpenCashSession] (bit, sin nulos, 0 por
--    omisión). Después de aplicar: ninguna fila. Después de rollback.sql: otra vez igual que antes.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[is_nullable] AS [AceptaNulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'RequireOpenCashSession';

-- 5. Antes y después: las filas. [Configuraciones] no cambia al aplicar ni al revertir.
SELECT COUNT(*) AS [Configuraciones] FROM [TenantSettings];

-- 6. Después: la migración quedó registrada (vacío antes de aplicar y después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261009064905_DropRequireOpenCashSession';
