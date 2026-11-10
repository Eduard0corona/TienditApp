-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 30B y las fases sin
--    migración que siguen, hasta la 31B), la más reciente es
--    20261108150147_AddOrderCancellationEmailsAndAnswerReminder. Si es anterior, revisa primero el
--    verify.sql de su carpeta: apply.sql también aplica las que falten. Después:
--    20261110022936_AddUniqueCategoryNamePerTenant.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. El índice único nuevo [IX_Categories_TenantId_Name] y la intercalación de [Categories].[Name],
--    con la que compara: con la de siempre (SQL_Latin1_General_CP1_CI_AS) no distingue mayúsculas,
--    espacios al final ni el ancho de las letras, toma «ß» por «ss» y sí distingue acentos. Antes de
--    aplicar y después de rollback.sql no hay índice
--    ([Indice] sale NULL).
SELECT c.[collation_name] AS [IntercalacionDeName],
    i.[name] AS [Indice], i.[is_unique] AS [Unico],
    (SELECT STRING_AGG(ic_c.[name], N',') WITHIN GROUP (ORDER BY ic.[key_ordinal])
     FROM sys.index_columns ic
     JOIN sys.columns ic_c ON ic_c.[object_id] = ic.[object_id] AND ic_c.[column_id] = ic.[column_id]
     WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]) AS [Columnas]
FROM sys.columns c
LEFT JOIN sys.indexes i ON i.[object_id] = c.[object_id] AND i.[name] = N'IX_Categories_TenantId_Name'
WHERE c.[object_id] = OBJECT_ID(N'dbo.Categories') AND c.[name] = N'Name';

-- 3. Antes (bloquea): categorías de un mismo negocio con el mismo nombre según esa intercalación,
--    como las compara el índice. Si devuelve filas, apply.sql se detiene con el error 50057 sin
--    cambiar nada. Renombra una de cada grupo antes de aplicar: el dueño desde Catálogo › Categorías
--    (editar el nombre de una repetida funciona), o con
--    database/production/data-repair/20261110_RepeatedCategoryNames (lee su README.md). [Conserva] = 1
--    es la que esa reparación deja con su nombre: la activa, y entre iguales la más antigua.
SELECT t.[Slug] AS [Negocio], c.[Name] AS [Nombre], c.[Id], c.[IsActive] AS [Activa],
    (SELECT COUNT(*) FROM [Products] p WHERE p.[CategoryId] = c.[Id]) AS [Productos],
    c.[CreatedAt] AS [Creada],
    CASE WHEN ROW_NUMBER() OVER (PARTITION BY c.[TenantId], c.[Name] ORDER BY c.[IsActive] DESC, c.[CreatedAt], c.[Id]) = 1
        THEN 1 ELSE 0 END AS [Conserva]
FROM [Categories] c
LEFT JOIN [Tenants] t ON t.[Id] = c.[TenantId]
WHERE EXISTS (
    SELECT 1 FROM [Categories] o
    WHERE o.[TenantId] = c.[TenantId] AND o.[Name] = c.[Name] AND o.[Id] <> c.[Id])
ORDER BY t.[Slug], c.[Name], [Conserva] DESC, c.[CreatedAt];

-- 4. Antes y después (no bloquea): nombres que la regla de la aplicación (Category.NameKey) tiene por
--    iguales, pero el índice no, porque solo difieren en espacios al inicio, espacios repetidos,
--    tabuladores, saltos de línea o espacios duros (una aproximación en SQL). Se quedan como están:
--    la aplicación deja editar su descripción u orden y solo revisa un nombre nuevo. Avisa al negocio
--    si conviene que renombre una.
WITH Normalized AS (
    SELECT c.[Id], c.[TenantId], c.[Name],
        LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(c.[Name] COLLATE Latin1_General_BIN2,
                NCHAR(9), N' '), NCHAR(10), N' '), NCHAR(13), N' '), NCHAR(160), N' '))),
            N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' ')) AS [NameKey]
    FROM [Categories] c
)
SELECT t.[Slug] AS [Negocio], n.[NameKey] AS [Clave], N'[' + n.[Name] + N']' AS [NombreEntreCorchetes], n.[Id]
FROM Normalized n
LEFT JOIN [Tenants] t ON t.[Id] = n.[TenantId]
WHERE EXISTS (
        SELECT 1 FROM Normalized o
        WHERE o.[TenantId] = n.[TenantId] AND o.[NameKey] = n.[NameKey] COLLATE Latin1_General_BIN2 AND o.[Id] <> n.[Id])
    AND NOT EXISTS (
        SELECT 1 FROM [Categories] o
        WHERE o.[TenantId] = n.[TenantId] AND o.[Name] = n.[Name] AND o.[Id] <> n.[Id])
ORDER BY t.[Slug], n.[NameKey], n.[Name];

-- 5. Antes y después: cuántas categorías y negocios hay, y cuántos grupos lista la consulta 3.
--    Después de aplicar son 0 (el índice ya no deja repetir); la migración no cambia filas.
SELECT (SELECT COUNT(*) FROM [Categories]) AS [Categorias],
    (SELECT COUNT(DISTINCT [TenantId]) FROM [Categories]) AS [NegociosConCategorias],
    (SELECT COUNT(*) FROM (SELECT 1 AS [x] FROM [Categories] GROUP BY [TenantId], [Name] HAVING COUNT(*) > 1) g) AS [GruposQueBloquean];
