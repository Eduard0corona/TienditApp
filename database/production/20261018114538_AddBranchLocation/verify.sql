-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24F), la más reciente
--    es 20261018081803_AddFavoriteBusinesses. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261018114538_AddBranchLocation.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas ([Branches].[Latitude] y [Longitude], decimal(9,6) con nulos) y
--    la restricción [CK_Branches_Location], cuya definición termina en
--    NOT ([Latitude]=(0) AND [Longitude]=(0)). Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.Branches') AND c.[name] IN (N'Latitude', N'Longitude')
ORDER BY c.[name];

SELECT [name] AS [Restriccion], [definition] AS [Definicion]
FROM sys.check_constraints
WHERE [parent_object_id] = OBJECT_ID(N'dbo.Branches') AND [name] = N'CK_Branches_Location';

-- 3. Después: cuántas sucursales tienen ubicación (rollback.sql se detiene con 50022 si hay
--    alguna) y cuántas no; de las que tienen, cuántas reciben clientes ([ServiceMode] 0, las
--    únicas cuya distancia ve un cliente). Justo después de aplicar: todas sin ubicación. Antes de
--    aplicar y después de rollback.sql las columnas no existen y la consulta no corre.
IF COL_LENGTH(N'dbo.Branches', N'Latitude') IS NOT NULL
    EXEC(N'SELECT
        SUM(CASE WHEN [Latitude] IS NOT NULL THEN 1 ELSE 0 END) AS [ConUbicacion],
        SUM(CASE WHEN [Latitude] IS NOT NULL AND [ServiceMode] = 0 THEN 1 ELSE 0 END) AS [ConUbicacionYLocal],
        SUM(CASE WHEN [Latitude] IS NULL THEN 1 ELSE 0 END) AS [SinUbicacion]
    FROM [Branches];');
