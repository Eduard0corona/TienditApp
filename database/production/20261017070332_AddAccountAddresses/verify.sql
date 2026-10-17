-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24A), la más reciente es
--    20261016162347_AddBusinessDirectory. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261017070332_AddAccountAddresses.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas de [AccountAddresses]: el nombre en nvarchar(60) ([Largo] va en bytes:
--    120), el código postal en varchar(5), la dirección en nvarchar(300) (600), las referencias en
--    nvarchar(500) (1000), y nulos solo en [References], [LastUsedAt], [CreatedBy], [UpdatedAt] y
--    [UpdatedBy]. Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.AccountAddresses')
ORDER BY c.[column_id];

-- 3. Después: la restricción CHECK CK_AccountAddresses_PostalCode, la llave
--    FK_AccountAddresses_AspNetUsers_UserId en cascada (CASCADE) y los índices: la llave primaria e
--    IX_AccountAddresses_UserId_LastUsedAt en [AccountAddresses], e IX_DeliveryZonePostalCodes_PostalCode
--    en [DeliveryZonePostalCodes] (no único, con [TenantId] y [DeliveryZoneId] incluidos: [Incluidas]
--    2). SQL Server muestra la CHECK a su manera (por ejemplo, (len([PostalCode])=(5) AND ...)).
--    Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT N'CHECK' AS [Tipo], OBJECT_NAME([parent_object_id]) AS [Tabla], [name] AS [Nombre], NULL AS [Unico],
    NULL AS [Incluidas], [definition] COLLATE DATABASE_DEFAULT AS [Definicion]
FROM sys.check_constraints
WHERE [parent_object_id] = OBJECT_ID(N'dbo.AccountAddresses')
UNION ALL
SELECT N'FK', OBJECT_NAME([parent_object_id]), [name], NULL, NULL, [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [parent_object_id] = OBJECT_ID(N'dbo.AccountAddresses')
UNION ALL
SELECT N'INDEX', OBJECT_NAME(i.[object_id]), i.[name], CAST(i.[is_unique] AS int),
    (SELECT COUNT(*) FROM sys.index_columns ic
     WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND ic.[is_included_column] = 1),
    NULL
FROM sys.indexes i
WHERE i.[type] > 0
    AND (i.[object_id] = OBJECT_ID(N'dbo.AccountAddresses')
        OR (i.[object_id] = OBJECT_ID(N'dbo.DeliveryZonePostalCodes') AND i.[name] = N'IX_DeliveryZonePostalCodes_PostalCode'))
ORDER BY [Tipo], [Tabla], [Nombre];

-- 4. Después de desplegar, y antes de rollback.sql: cuántas direcciones guardaron las cuentas, en
--    cuántas cuentas, y la que más tiene (nunca más de 10). Con alguna dirección, rollback.sql se
--    detiene (error 50017) sin cambiar nada. Recién aplicado: 0 direcciones.
IF OBJECT_ID(N'dbo.AccountAddresses') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [Direcciones], COUNT(DISTINCT [UserId]) AS [Cuentas],
    ISNULL((SELECT TOP (1) COUNT(*) FROM [AccountAddresses] GROUP BY [UserId] ORDER BY COUNT(*) DESC), 0) AS [MaximoPorCuenta]
FROM [AccountAddresses];');

-- 5. Después: consistencia. Cada consulta debe dar 0 filas:
--    a. direcciones de una cuenta que no es de cliente (solo las cuentas de cliente guardan direcciones;
--       [Role] 4 es Customer);
--    b. cuentas con más de 10 direcciones.
IF OBJECT_ID(N'dbo.AccountAddresses') IS NOT NULL
BEGIN
    EXEC(N'SELECT a.[UserId], u.[Role] AS [Rol], COUNT(*) AS [Direcciones]
FROM [AccountAddresses] a
INNER JOIN [AspNetUsers] u ON u.[Id] = a.[UserId]
WHERE u.[Role] <> 4
GROUP BY a.[UserId], u.[Role];');
    EXEC(N'SELECT [UserId], COUNT(*) AS [Direcciones]
FROM [AccountAddresses]
GROUP BY [UserId]
HAVING COUNT(*) > 10;');
END;
