-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 32F), la más reciente es
--    20261116030110_AddAssistantConnections. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261124213159_AddSupportAccesses.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes de rollback.sql: cuántos accesos de soporte hay, cuántos siguen abiertos y a cuántos negocios.
--    Justo después de aplicar todo sale en 0. rollback.sql se detiene con el error 50066, sin cambiar
--    nada, mientras haya uno: borraría el historial de quién entró a qué negocio y por qué (los registros
--    de Seguridad y actividad de cada negocio lo conservan, pero la tabla es la que da el acceso). Con
--    uno así, no reviertas: vuelve al código anterior sin rollback.sql (no conoce la tabla).
--    Antes de aplicar y después de rollback.sql la tabla no existe y esta parte no corre.
IF OBJECT_ID(N'dbo.SupportAccesses') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [Accesos],
        COALESCE(SUM(CASE WHEN [EndedAt] IS NULL AND [ExpiresAt] > SYSDATETIMEOFFSET() THEN 1 ELSE 0 END), 0) AS [Abiertos],
        COUNT(DISTINCT [TenantId]) AS [Negocios]
        FROM [SupportAccesses];');

-- 3. Después: la tabla [SupportAccesses] con sus columnas; sus llaves (a [Tenants] y a [AspNetUsers],
--    sin acción al borrar); sus índices (el del acceso abierto de un administrador a un negocio y el de
--    los accesos de un administrador) y sus restricciones (el largo del motivo, que venza después de empezar
--    y el cierre dentro de su tiempo). Antes de aplicar y después de rollback.sql no devuelven filas.
SELECT c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo], c.[max_length] AS [Largo], c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.SupportAccesses')
ORDER BY c.[column_id];

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Referencia], fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] = OBJECT_ID(N'dbo.SupportAccesses')
ORDER BY fk.[name];

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.SupportAccesses') AND i.[name] LIKE N'IX[_]%'
ORDER BY i.[name];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[parent_object_id] = OBJECT_ID(N'dbo.SupportAccesses')
ORDER BY cc.[name];

-- 4. Antes y después: cuántos negocios y cuántas cuentas hay, y cuántas son de la plataforma. La
--    migración no cambia filas: solo cambian por lo que pase entre las dos corridas.
SELECT (SELECT COUNT(*) FROM [Tenants]) AS [Negocios],
    (SELECT COUNT(*) FROM [AspNetUsers]) AS [Cuentas],
    (SELECT COUNT(*) FROM [AspNetUsers] WHERE [Role] = 9) AS [AdministradoresDePlataforma];
