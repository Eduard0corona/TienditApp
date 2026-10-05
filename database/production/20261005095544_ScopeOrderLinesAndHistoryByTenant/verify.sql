-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas: nunca muestran el
-- valor ni el hash de un token.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261005045624_AddPurchaseOrderClosing (Fase 6; la Fase 7 no tuvo migraciones); si es
--    anterior, apply.sql también aplicará las que falten (revisa primero el verify.sql de cada
--    carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: las columnas de la fase. Antes de aplicar (y después de rollback.sql) solo
--    devuelve RefreshTokens.Token, sin nulos (AceptaNulos = 0). Después, Token acepta nulos y
--    aparecen RefreshTokens.RevocationReason (int, con nulos), SessionId (uniqueidentifier) y
--    TokenHash (varchar(64), con nulos), y TenantId (uniqueidentifier) en OrderItems y en
--    OrderStatusHistory.
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], t.[name] AS [Tipo],
    c.[max_length] AS [Bytes], c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE (c.[object_id] = OBJECT_ID(N'dbo.RefreshTokens')
        AND c.[name] IN (N'Token', N'TokenHash', N'SessionId', N'RevocationReason'))
    OR (c.[object_id] IN (OBJECT_ID(N'dbo.OrderItems'), OBJECT_ID(N'dbo.OrderStatusHistory'))
        AND c.[name] = N'TenantId')
ORDER BY [Tabla], [Columna];

-- 3. Antes y después: refresh tokens. Antes de aplicar y justo después, todos [EnTexto] (siguen
--    funcionando con su valor). Ya desplegada la versión nueva, los convierte a hash al arrancar
--    (con los jobs activos) o al usarlos: [EnTexto] y [ReemplazoEnTexto] bajan a 0. [TokenNulo]
--    debe ser 0 siempre: la versión anterior lee [Token] como obligatorio.
SELECT
    COUNT(*) AS [Tokens],
    COUNT(CASE WHEN [Token] <> N'' THEN 1 END) AS [EnTexto],
    COUNT(CASE WHEN [ReplacedByToken] IS NOT NULL THEN 1 END) AS [ReemplazoEnTexto],
    COUNT(CASE WHEN [Token] IS NULL THEN 1 END) AS [TokenNulo],
    COUNT(CASE WHEN [RevokedAt] IS NULL AND [ExpiresAt] > SYSDATETIMEOFFSET() THEN 1 END) AS [Vigentes]
FROM [RefreshTokens];

-- 4. Después: tokens con hash y sesiones (no devuelve nada antes de aplicar ni después de
--    rollback.sql: EXEC evita que la consulta falle sin columnas). Justo después de aplicar,
--    [ConHash] = 0 y cada token es su propia sesión ([Sesiones] = [Tokens] de la consulta 3).
--    [ReusoDetectado] cuenta los tokens cuya reutilización terminó su sesión.
IF COL_LENGTH(N'dbo.RefreshTokens', N'TokenHash') IS NOT NULL
    EXEC(N'SELECT
    COUNT(CASE WHEN [TokenHash] IS NOT NULL THEN 1 END) AS [ConHash],
    COUNT(CASE WHEN [TokenHash] IS NULL AND [Token] <> N'''' THEN 1 END) AS [SinHashEnTexto],
    COUNT(DISTINCT [SessionId]) AS [Sesiones],
    COUNT(CASE WHEN [RevocationReason] = 3 THEN 1 END) AS [ReusoDetectado]
FROM [RefreshTokens];');

-- 5. Después: partidas e historial de estado sin negocio (TenantId vacío) o con uno distinto al
--    de su orden. Justo después de aplicar, los dos en 0. Mientras siga publicada la versión
--    anterior, [SinNegocio] puede crecer (lo que agrega toma el valor vacío y el filtro por
--    negocio lo oculta); la versión nueva lo completa al arrancar. [OtroNegocio] debe ser 0
--    siempre.
IF COL_LENGTH(N'dbo.OrderItems', N'TenantId') IS NOT NULL
    EXEC(N'SELECT N''OrderItems'' AS [Tabla],
    COUNT(*) AS [Filas],
    COUNT(CASE WHEN i.[TenantId] = ''00000000-0000-0000-0000-000000000000'' THEN 1 END) AS [SinNegocio],
    COUNT(CASE WHEN i.[TenantId] <> ''00000000-0000-0000-0000-000000000000''
        AND i.[TenantId] <> o.[TenantId] THEN 1 END) AS [OtroNegocio]
FROM [OrderItems] i
INNER JOIN [Orders] o ON o.[Id] = i.[OrderId]
UNION ALL
SELECT N''OrderStatusHistory'',
    COUNT(*),
    COUNT(CASE WHEN h.[TenantId] = ''00000000-0000-0000-0000-000000000000'' THEN 1 END),
    COUNT(CASE WHEN h.[TenantId] <> ''00000000-0000-0000-0000-000000000000''
        AND h.[TenantId] <> o.[TenantId] THEN 1 END)
FROM [OrderStatusHistory] h
INNER JOIN [Orders] o ON o.[Id] = h.[OrderId];');

-- 6. Antes y después: totales. Aplicar no agrega, borra ni cambia órdenes, partidas, importes,
--    historial ni tokens: compara estos números con los de antes.
SELECT
    (SELECT COUNT(*) FROM [Orders]) AS [Ordenes],
    (SELECT COUNT(*) FROM [OrderItems]) AS [Partidas],
    (SELECT ISNULL(SUM([TotalPrice]), 0) FROM [OrderItems]) AS [ImportePartidas],
    (SELECT COUNT(*) FROM [OrderStatusHistory]) AS [CambiosDeEstado],
    (SELECT COUNT(*) FROM [RefreshTokens]) AS [RefreshTokens];

-- 7. Después: los índices nuevos (vacío antes de aplicar y después de rollback.sql):
--    IX_RefreshTokens_SessionId, IX_RefreshTokens_TokenHash (único, filtrado por
--    [TokenHash] IS NOT NULL), IX_OrderItems_TenantId_OrderId e
--    IX_OrderStatusHistory_TenantId_OrderId.
SELECT OBJECT_NAME(i.[object_id]) AS [Tabla], i.[name] AS [Indice], i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[name] IN (N'IX_RefreshTokens_SessionId', N'IX_RefreshTokens_TokenHash',
    N'IX_OrderItems_TenantId_OrderId', N'IX_OrderStatusHistory_TenantId_OrderId')
ORDER BY [Tabla], [Indice];

-- 8. Después: las dos migraciones quedaron registradas (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] IN (N'20261005092153_RevocableSessions', N'20261005095544_ScopeOrderLinesAndHistoryByTenant')
ORDER BY [MigrationId];
