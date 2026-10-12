-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22G), la más reciente es
--    20261012145908_AddPlatformSettlements. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261012204959_AddBranchServiceMode.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [ServiceMode] de [Branches] (int, sin nulos, con su DEFAULT ((0))) y la
--    restricción CK_Branches_ServiceMode ([ServiceMode] IN (0, 1)). Antes de aplicar y después de
--    rollback.sql: ninguna fila.
SELECT N'COLUMNA' AS [Tipo], c.[name] AS [Nombre], t.[name] AS [Definicion], c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.Branches') AND c.[name] = N'ServiceMode'
UNION ALL
SELECT N'DEFAULT', d.[name], d.[definition] COLLATE DATABASE_DEFAULT, NULL
FROM sys.default_constraints d
INNER JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
WHERE d.[parent_object_id] = OBJECT_ID(N'dbo.Branches') AND c.[name] = N'ServiceMode'
UNION ALL
SELECT N'CHECK', [name], [definition] COLLATE DATABASE_DEFAULT, NULL
FROM sys.check_constraints
WHERE [parent_object_id] = OBJECT_ID(N'dbo.Branches') AND [name] = N'CK_Branches_ServiceMode'
ORDER BY [Tipo];

-- 3. Después de desplegar, y antes de volver a la versión anterior o de rollback.sql: las
--    sucursales por cómo atienden ([Modo] 0 local abierto al público, 1 solo a domicilio), cuántas son
--    la principal de su negocio y cuántos negocios las tienen; y cuáles son las solo a domicilio (su
--    negocio, su nombre y si es la principal; nunca su dirección). Con alguna, rollback.sql se detiene
--    (error 50011) sin cambiar nada, y la versión anterior publicaría la dirección de las principales
--    en el menú en línea. Recién aplicado: todas en 0 y la lista vacía.
IF COL_LENGTH(N'dbo.Branches', N'ServiceMode') IS NOT NULL
BEGIN
    EXEC(N'SELECT [ServiceMode] AS [Modo], COUNT(*) AS [Sucursales],
    SUM(CASE WHEN [IsDefault] = 1 THEN 1 ELSE 0 END) AS [Principales], COUNT(DISTINCT [TenantId]) AS [Negocios]
FROM [Branches]
GROUP BY [ServiceMode]
ORDER BY [Modo];');
    EXEC(N'SELECT b.[TenantId] AS [Negocio], t.[Name] AS [NombreDelNegocio], b.[Id] AS [Sucursal], b.[Name] AS [Nombre],
    b.[IsDefault] AS [Principal]
FROM [Branches] b
INNER JOIN [Tenants] t ON t.[Id] = b.[TenantId]
WHERE b.[ServiceMode] = 1
ORDER BY t.[Name], b.[IsDefault] DESC, b.[Name];');
END;

-- 4. Después: consistencia. Debe dar 0 filas: sucursales con un modo que no es 0 ni 1 (la
--    restricción CHECK no deja escribirlos).
IF COL_LENGTH(N'dbo.Branches', N'ServiceMode') IS NOT NULL
    EXEC(N'SELECT [Id], [TenantId] AS [Negocio], [ServiceMode] AS [Modo]
FROM [Branches]
WHERE [ServiceMode] NOT IN (0, 1);');
