-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas: nunca muestran el
-- valor ni el hash de un token.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261005175643_StopUsingPlainRefreshTokenValues (Fase 9); si es anterior, apply.sql también
--    aplicará las que falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes: lo que depende de [Token] y [ReplacedByToken]. Debe salir solo el índice
--    IX_RefreshTokens_Token (columna Token), que apply.sql borra, y ninguna estadística ni objeto.
--    Hay que resolver antes de aplicar: otro índice; una estadística con [Automatica] = 0 (creada a
--    mano: bloquea el borrado, se quita con DROP STATISTICS); o una vista, función o procedimiento
--    que use la tabla. Las estadísticas automáticas se borran solas con la columna.
--    Después de aplicar: ninguna fila.
SELECT i.[name] AS [Indice], c.[name] AS [Columna]
FROM sys.index_columns ic
INNER JOIN sys.indexes i ON i.[object_id] = ic.[object_id] AND i.[index_id] = ic.[index_id]
INNER JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
WHERE ic.[object_id] = OBJECT_ID(N'dbo.RefreshTokens') AND c.[name] IN (N'Token', N'ReplacedByToken')
ORDER BY [Indice], [Columna];

SELECT s.[name] AS [Estadistica], c.[name] AS [Columna], s.[auto_created] AS [Automatica]
FROM sys.stats s
INNER JOIN sys.stats_columns sc ON sc.[object_id] = s.[object_id] AND sc.[stats_id] = s.[stats_id]
INNER JOIN sys.columns c ON c.[object_id] = sc.[object_id] AND c.[column_id] = sc.[column_id]
WHERE s.[object_id] = OBJECT_ID(N'dbo.RefreshTokens')
    AND c.[name] IN (N'Token', N'ReplacedByToken')
    AND NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.[object_id] = s.[object_id] AND i.[index_id] = s.[stats_id])
ORDER BY [Estadistica];

-- (La tabla misma no cuenta: su índice filtrado la referencia.)
SELECT OBJECT_SCHEMA_NAME(d.[referencing_id]) + N'.' + OBJECT_NAME(d.[referencing_id]) AS [ObjetoQueUsaLaTabla]
FROM sys.sql_expression_dependencies d
WHERE d.[referenced_id] = OBJECT_ID(N'dbo.RefreshTokens') AND d.[referencing_id] <> d.[referenced_id];

-- 3. Antes: valores en texto que queden. Deben ser 0: la Fase 9 los convirtió a hash y no escribe
--    esas columnas. Si no lo son, sigue la nota de apply.sql (el rollback.sql de la carpeta de la
--    Fase 9 y luego apply.sql). Después de aplicar no sale: las columnas ya no existen.
IF COL_LENGTH(N'dbo.RefreshTokens', N'Token') IS NOT NULL
    AND COL_LENGTH(N'dbo.RefreshTokens', N'ReplacedByToken') IS NOT NULL
    EXEC(N'SELECT
    COUNT(CASE WHEN [Token] <> N'''' THEN 1 END) AS [EnTexto],
    COUNT(CASE WHEN [ReplacedByToken] IS NOT NULL THEN 1 END) AS [ReemplazoEnTexto]
FROM [RefreshTokens];');

-- 4. Antes y después: las columnas y su índice. Antes: las dos columnas y el índice. Después de
--    aplicar: nada. Después de rollback.sql: otra vez las dos, con nulos (AceptaNulos = 1), y el
--    índice.
SELECT c.[name] AS [Columna], t.[name] AS [Tipo], c.[max_length] AS [Bytes], c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.RefreshTokens') AND c.[name] IN (N'Token', N'ReplacedByToken')
ORDER BY [Columna];

SELECT [name] AS [Indice]
FROM sys.indexes
WHERE [object_id] = OBJECT_ID(N'dbo.RefreshTokens') AND [name] = N'IX_RefreshTokens_Token';

-- 5. Antes y después: las sesiones. [Tokens], [Sesiones] y [SinHash] no cambian al aplicar ni al
--    revertir ([Vigentes] solo baja con el tiempo).
SELECT
    COUNT(*) AS [Tokens],
    COUNT(DISTINCT [SessionId]) AS [Sesiones],
    COUNT(CASE WHEN [RevokedAt] IS NULL AND [ExpiresAt] > SYSDATETIMEOFFSET() THEN 1 END) AS [Vigentes],
    COUNT(CASE WHEN [TokenHash] IS NULL THEN 1 END) AS [SinHash]
FROM [RefreshTokens];

-- 6. Después: la migración quedó registrada (vacío antes de aplicar y después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns';
