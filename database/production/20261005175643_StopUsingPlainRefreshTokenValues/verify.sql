-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas: nunca muestran el
-- valor ni el hash de un token.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261005095544_ScopeOrderLinesAndHistoryByTenant (Fase 8); si es anterior, apply.sql también
--    aplicará las que falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: refresh tokens.
--    [EnTexto] y [ReemplazoEnTexto]: valores que aún se guardan en texto. Con la Fase 8 desplegada
--    y sus jobs activos suelen estar en 0 desde antes; después de aplicar, siempre en 0, igual que
--    [SinHashEnTexto] (esos tokens recibieron su hash y sus sesiones siguen abiertas).
--    [SinHash] que quede: filas sin valor ni hash que dejó un rollback de la Fase 8; no abren
--    ninguna sesión. [Rotados] sube en los tokens reemplazados que estaban en texto.
--    [TokenNulo]: 0 justo después de aplicar; crece cuando la versión nueva emite tokens (ya no
--    escribe esa columna), lo que la Fase 8 tolera.
--    [Tokens], [Sesiones] y [Vigentes] no cambian al aplicar.
IF COL_LENGTH(N'dbo.RefreshTokens', N'Token') IS NOT NULL
    EXEC(N'SELECT
    COUNT(*) AS [Tokens],
    COUNT(DISTINCT [SessionId]) AS [Sesiones],
    COUNT(CASE WHEN [RevokedAt] IS NULL AND [ExpiresAt] > SYSDATETIMEOFFSET() THEN 1 END) AS [Vigentes],
    COUNT(CASE WHEN [Token] <> N'''' THEN 1 END) AS [EnTexto],
    COUNT(CASE WHEN [ReplacedByToken] IS NOT NULL THEN 1 END) AS [ReemplazoEnTexto],
    COUNT(CASE WHEN [TokenHash] IS NULL THEN 1 END) AS [SinHash],
    COUNT(CASE WHEN [TokenHash] IS NULL AND [Token] <> N'''' THEN 1 END) AS [SinHashEnTexto],
    COUNT(CASE WHEN [RevocationReason] = 1 THEN 1 END) AS [Rotados],
    COUNT(CASE WHEN [Token] IS NULL THEN 1 END) AS [TokenNulo]
FROM [RefreshTokens];');

-- 3. Antes y después: las columnas que la versión nueva deja de usar siguen ahí, con nulos
--    (AceptaNulos = 1), y también su índice IX_RefreshTokens_Token, hasta que una fase posterior
--    los borre.
SELECT c.[name] AS [Columna], t.[name] AS [Tipo], c.[max_length] AS [Bytes], c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.RefreshTokens') AND c.[name] IN (N'Token', N'ReplacedByToken')
ORDER BY [Columna];

SELECT [name] AS [Indice]
FROM sys.indexes
WHERE [object_id] = OBJECT_ID(N'dbo.RefreshTokens') AND [name] = N'IX_RefreshTokens_Token';

-- 4. Después: la migración quedó registrada (vacío antes de aplicar y después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261005175643_StopUsingPlainRefreshTokenValues';
