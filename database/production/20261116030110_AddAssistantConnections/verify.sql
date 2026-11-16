-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 32D), la más reciente es
--    20261115151611_AddAssistantOrders. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261116030110_AddAssistantConnections.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes de rollback.sql: cuántos asistentes registrados hay, cuántas conexiones (activas y
--    desconectadas) y cuántas cuentas tienen uno conectado. Justo después de aplicar todo sale en 0.
--    rollback.sql se detiene con el error 50065, sin cambiar nada, mientras haya una conexión activa:
--    esos clientes perderían su asistente sin aviso. Con una así, no reviertas: vuelve al código
--    anterior sin rollback.sql (sus asistentes dejan de entrar y vuelven cuando regrese esta versión).
--    Para revertir de todos modos, guarda esta salida y solo entonces desconéctalos:
--        UPDATE [AssistantConnections] SET [RevokedAt] = SYSDATETIMEOFFSET() WHERE [RevokedAt] IS NULL;
--    Antes de aplicar y después de rollback.sql las tablas no existen y esta parte no corre.
IF OBJECT_ID(N'dbo.AssistantConnections') IS NOT NULL
    EXEC(N'SELECT (SELECT COUNT(*) FROM [AssistantClients]) AS [Asistentes],
        (SELECT COUNT(*) FROM [AssistantConnections] WHERE [RevokedAt] IS NULL) AS [ConexionesActivas],
        (SELECT COUNT(*) FROM [AssistantConnections] WHERE [RevokedAt] IS NOT NULL) AS [Desconectadas],
        (SELECT COUNT(DISTINCT [UserId]) FROM [AssistantConnections] WHERE [RevokedAt] IS NULL) AS [Cuentas];');

-- 3. Después: las tablas [AssistantClients], [AssistantAuthorizationCodes], [AssistantConnections] y
--    [AssistantTokens] con sus columnas; sus llaves (a [AspNetUsers] en cascada, la del cliente de una
--    conexión sin cascada y la de la conexión de un código sin acción); sus índices (los hash, el
--    identificador del cliente y la conexión activa por cuenta y asistente, únicos; los de vencimiento
--    y de la limpieza) y sus restricciones (largo de los hash, del identificador y del code_challenge,
--    el tipo de token, la rotación solo en los de actualización y el código canjeado con su conexión).
--    Antes de aplicar y después de rollback.sql no devuelven filas.
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo], c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE c.[object_id] IN (OBJECT_ID(N'dbo.AssistantClients'), OBJECT_ID(N'dbo.AssistantAuthorizationCodes'),
    OBJECT_ID(N'dbo.AssistantConnections'), OBJECT_ID(N'dbo.AssistantTokens'))
ORDER BY [Tabla], c.[column_id];

SELECT OBJECT_NAME(fk.[parent_object_id]) AS [Tabla], fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Referencia],
    fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] IN (OBJECT_ID(N'dbo.AssistantClients'), OBJECT_ID(N'dbo.AssistantAuthorizationCodes'),
    OBJECT_ID(N'dbo.AssistantConnections'), OBJECT_ID(N'dbo.AssistantTokens'))
ORDER BY [Tabla], fk.[name];

SELECT OBJECT_NAME(i.[object_id]) AS [Tabla], i.[name] AS [Indice], i.[is_unique] AS [Unico], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] IN (OBJECT_ID(N'dbo.AssistantClients'), OBJECT_ID(N'dbo.AssistantAuthorizationCodes'),
    OBJECT_ID(N'dbo.AssistantConnections'), OBJECT_ID(N'dbo.AssistantTokens'))
    AND i.[name] LIKE N'IX[_]%'
ORDER BY [Tabla], i.[name];

SELECT OBJECT_NAME(cc.[parent_object_id]) AS [Tabla], cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[parent_object_id] IN (OBJECT_ID(N'dbo.AssistantClients'), OBJECT_ID(N'dbo.AssistantAuthorizationCodes'),
    OBJECT_ID(N'dbo.AssistantConnections'), OBJECT_ID(N'dbo.AssistantTokens'))
ORDER BY [Tabla], cc.[name];

-- 4. Antes y después: cuántas cuentas hay y cuántas son de clientes. La migración no cambia filas:
--    solo cambian por lo que pase entre las dos corridas.
SELECT (SELECT COUNT(*) FROM [AspNetUsers]) AS [Cuentas],
    (SELECT COUNT(*) FROM [AspNetUsers] WHERE [Role] = 4) AS [Clientes];
