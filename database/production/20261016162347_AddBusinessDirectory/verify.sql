-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 23A, sin migración
--    propia), la más reciente es 20261013191311_AddHomeVisits. Si es anterior, revisa primero el
--    verify.sql de su carpeta: apply.sql también aplica las que falten. Después:
--    20261016162347_AddBusinessDirectory.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas ([TenantSettings].[ListedInDirectory] bit sin nulos;
--    [Tenants].[DirectoryCategory] int sin nulos y [PublicDescription] nvarchar(160) con nulos).
--    [Largo] va en bytes (nvarchar(160) es 320). Antes de aplicar y después de rollback.sql:
--    ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'ListedInDirectory')
    OR (c.[object_id] = OBJECT_ID(N'dbo.Tenants') AND c.[name] IN (N'DirectoryCategory', N'PublicDescription'))
ORDER BY [Tabla], c.[column_id];

-- 3. Después: las restricciones nuevas: las CHECK CK_Tenants_DirectoryCategory (de 0 a 10) y
--    CK_Tenants_PublicDescription (nula o no vacía), y los DEFAULT de [ListedInDirectory] ((1)) y
--    de [DirectoryCategory] ((10)). SQL Server muestra las CHECK a su manera. Antes de aplicar y
--    después de rollback.sql: ninguna fila.
SELECT OBJECT_NAME([parent_object_id]) AS [Tabla], N'CHECK' AS [Tipo], [name] AS [Nombre],
    [definition] COLLATE DATABASE_DEFAULT AS [Definicion]
FROM sys.check_constraints
WHERE [name] IN (N'CK_Tenants_DirectoryCategory', N'CK_Tenants_PublicDescription')
UNION ALL
SELECT OBJECT_NAME(d.[parent_object_id]), N'DEFAULT', d.[name], d.[definition] COLLATE DATABASE_DEFAULT
FROM sys.default_constraints d
INNER JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
WHERE (d.[parent_object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'ListedInDirectory')
    OR (d.[parent_object_id] = OBJECT_ID(N'dbo.Tenants') AND c.[name] = N'DirectoryCategory')
ORDER BY [Tabla], [Tipo], [Nombre];

-- 4. Antes de aplicar: la categoría que apply.sql dará a los negocios, por giro y tipo (la misma
--    regla que BusinessCategoryDefaults.Suggest), y cuántos aparecerán en Explorar (activos y con
--    el menú en línea publicado: [OnlineMenuMode] 1 o 2). Revisa los giros escritos a mano: toman
--    la categoría de su tipo y el dueño la puede cambiar. [Tipo] 0 Alimentos, 1 Comercio,
--    2 Servicios; [Categoria] 0 Comida, 1 Abarrotes, 2 Farmacia y salud, 3 Ropa y accesorios,
--    4 Electrónica, 5 Ferretería y hogar, 6 Papelería, 7 Belleza y bienestar, 8 Autos, 9 Servicios,
--    10 Otros. Después de aplicar, la misma consulta con la categoría guardada.
IF COL_LENGTH(N'dbo.Tenants', N'DirectoryCategory') IS NULL
    SELECT t.[Industry] AS [Giro], t.[BusinessType] AS [Tipo],
        CASE
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI IN (N'Alimentos y bebidas', N'Comida a domicilio') THEN 0
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI = N'Abarrotes' THEN 1
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI IN (N'Farmacia', N'Consultorio') THEN 2
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI = N'Ropa y accesorios' THEN 3
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI = N'Electronica' THEN 4
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI = N'Ferreteria' THEN 5
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI = N'Papeleria' THEN 6
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI IN (N'Estetica y spa', N'Gimnasio') THEN 7
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI = N'Taller mecanico' THEN 8
            WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI IN (N'Servicios profesionales', N'Educacion', N'Lavanderia') THEN 9
            WHEN t.[BusinessType] = 0 THEN 0
            WHEN t.[BusinessType] = 2 THEN 9
            ELSE 10
        END AS [Categoria],
        COUNT(*) AS [Negocios],
        SUM(CASE WHEN t.[Status] = 0 AND s.[OnlineMenuMode] IN (1, 2) THEN 1 ELSE 0 END) AS [EnExplorar]
    FROM [Tenants] t
    LEFT JOIN [TenantSettings] s ON s.[TenantId] = t.[Id]
    GROUP BY t.[Industry], t.[BusinessType]
    ORDER BY [Categoria], [Negocios] DESC;
ELSE
    EXEC(N'SELECT t.[Industry] AS [Giro], t.[BusinessType] AS [Tipo], t.[DirectoryCategory] AS [Categoria],
    COUNT(*) AS [Negocios],
    SUM(CASE WHEN t.[Status] = 0 AND s.[OnlineMenuMode] IN (1, 2) AND s.[ListedInDirectory] = 1 THEN 1 ELSE 0 END) AS [EnExplorar]
FROM [Tenants] t
LEFT JOIN [TenantSettings] s ON s.[TenantId] = t.[Id]
GROUP BY t.[Industry], t.[BusinessType], t.[DirectoryCategory]
ORDER BY [Categoria], [Negocios] DESC;');

-- 5. Después de desplegar, y antes de rollback.sql: lo que decidieron los dueños en Explorar, por
--    negocio: si lo ocultaron, si escribieron su descripción y cuántos cambios guarda la bitácora
--    (DirectoryProfileUpdated). Con alguna fila, rollback.sql se detiene (error 50016) sin cambiar
--    nada. Recién aplicado: ninguna fila.
IF COL_LENGTH(N'dbo.Tenants', N'DirectoryCategory') IS NOT NULL
    EXEC(N'SELECT t.[Id] AS [Negocio], t.[Name] AS [Nombre],
    CASE WHEN s.[ListedInDirectory] = 0 THEN 1 ELSE 0 END AS [Oculto],
    CASE WHEN t.[PublicDescription] IS NOT NULL THEN 1 ELSE 0 END AS [ConDescripcion],
    (SELECT COUNT(*) FROM [AuditLogs] a WHERE a.[TenantId] = t.[Id] AND a.[Action] = N''DirectoryProfileUpdated'') AS [Cambios]
FROM [Tenants] t
LEFT JOIN [TenantSettings] s ON s.[TenantId] = t.[Id]
WHERE s.[ListedInDirectory] = 0
    OR t.[PublicDescription] IS NOT NULL
    OR EXISTS (SELECT 1 FROM [AuditLogs] a WHERE a.[TenantId] = t.[Id] AND a.[Action] = N''DirectoryProfileUpdated'')
ORDER BY t.[Name];');

-- 6. Después: consistencia. Debe dar 0 filas: descripciones con saltos de línea o tabuladores, que
--    la app no escribe (la descripción va en un solo renglón).
IF COL_LENGTH(N'dbo.Tenants', N'PublicDescription') IS NOT NULL
    EXEC(N'SELECT [Id], [Name]
FROM [Tenants]
WHERE [PublicDescription] LIKE N''%'' + CHAR(10) + N''%''
    OR [PublicDescription] LIKE N''%'' + CHAR(13) + N''%''
    OR [PublicDescription] LIKE N''%'' + CHAR(9) + N''%'';');
