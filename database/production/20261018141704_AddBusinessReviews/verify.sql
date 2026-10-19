-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24G), la más reciente
--    es 20261018114538_AddBranchLocation. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261018141704_AddBusinessReviews.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla nueva con sus índices (el único por cuenta y negocio, el de la tienda y el
--    filtrado de las reportadas) y sus cinco restricciones. Antes de aplicar y después de
--    rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.BusinessReviews') AND i.[name] IS NOT NULL
ORDER BY i.[name];

SELECT [name] AS [Restriccion]
FROM sys.check_constraints
WHERE [parent_object_id] = OBJECT_ID(N'dbo.BusinessReviews')
ORDER BY [name];

-- 3. Después: cuántas reseñas hay (rollback.sql se detiene con 50023 si hay una), cuántas están
--    ocultas, cuántas esperan a la plataforma y cuántas tienen respuesta. Justo después de aplicar:
--    ninguna. Antes de aplicar y después de rollback.sql la tabla no existe y la consulta no corre.
IF OBJECT_ID(N'dbo.BusinessReviews') IS NOT NULL
    EXEC(N'SELECT
        COUNT(*) AS [Resenas],
        SUM(CASE WHEN [Status] = 1 THEN 1 ELSE 0 END) AS [Ocultas],
        SUM(CASE WHEN [ReportedAt] IS NOT NULL THEN 1 ELSE 0 END) AS [Reportadas],
        SUM(CASE WHEN [Reply] IS NOT NULL THEN 1 ELSE 0 END) AS [ConRespuesta]
    FROM [BusinessReviews];');
