-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de repair.sql.
-- Lista las categorías de un mismo negocio con el mismo nombre según la intercalación de
-- [Categories].[Name] (como compara IX_Categories_TenantId_Name) y el nombre que repair.sql daría a
-- cada una. No modifican nada. Ver README.md.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Cuántos grupos de nombres repetidos hay y cuántas categorías renombraría repair.sql. Después
--    de repair.sql, los dos en 0.
SELECT COUNT(*) AS [Grupos], ISNULL(SUM([Repetidas] - 1), 0) AS [PorRenombrar]
FROM (SELECT COUNT(*) AS [Repetidas] FROM [Categories] GROUP BY [TenantId], [Name] HAVING COUNT(*) > 1) g;

-- 2. Cada categoría repetida, la que conserva su nombre primero ([Lugar] = 1: la activa y, entre
--    iguales, la más antigua) y las demás en el orden en que repair.sql las numera desde «(2)». El
--    número final puede ser mayor si el negocio ya tiene ese nombre (repair.sql busca el siguiente
--    libre). [Productos] ayuda a decidir con el negocio cuál renombrar a mano.
SELECT t.[Slug] AS [Negocio], c.[Name] AS [Nombre], c.[Id], c.[IsActive] AS [Activa],
    (SELECT COUNT(*) FROM [Products] p WHERE p.[CategoryId] = c.[Id]) AS [Productos],
    c.[CreatedAt] AS [Creada],
    ROW_NUMBER() OVER (PARTITION BY c.[TenantId], c.[Name] ORDER BY c.[IsActive] DESC, c.[CreatedAt], c.[Id]) AS [Lugar]
FROM [Categories] c
LEFT JOIN [Tenants] t ON t.[Id] = c.[TenantId]
WHERE EXISTS (
    SELECT 1 FROM [Categories] o
    WHERE o.[TenantId] = c.[TenantId] AND o.[Name] = c.[Name] AND o.[Id] <> c.[Id])
ORDER BY t.[Slug], c.[Name], [Lugar];
