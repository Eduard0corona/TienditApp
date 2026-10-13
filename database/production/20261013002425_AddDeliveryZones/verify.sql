-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22A), la más reciente es
--    20261012204959_AddBranchServiceMode. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261013002425_AddDeliveryZones.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas de [DeliveryZones] y [DeliveryZonePostalCodes]: los montos en
--    decimal(18,2), el nombre en nvarchar(60) ([Largo] va en bytes: 120), la revisión en int, el
--    código postal en varchar(5), y nulos solo en [FreeDeliveryFrom], [CreatedBy], [UpdatedAt] y [UpdatedBy]. Antes de
--    aplicar y después de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [Largo],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] IN (OBJECT_ID(N'dbo.DeliveryZones'), OBJECT_ID(N'dbo.DeliveryZonePostalCodes'))
ORDER BY [Tabla], c.[column_id];

-- 3. Después: las restricciones CHECK, cuatro de [DeliveryZones] (_Fee, _FreeDeliveryFrom,
--    _MinimumOrder y _Revision) y una de [DeliveryZonePostalCodes] (_PostalCode); la llave
--    FK_DeliveryZonePostalCodes_DeliveryZones_DeliveryZoneId en cascada (CASCADE); y los índices: las
--    dos llaves primarias, IX_DeliveryZonePostalCodes_TenantId_PostalCode ([Unico] 1),
--    IX_DeliveryZonePostalCodes_DeliveryZoneId e IX_DeliveryZones_TenantId_Name. SQL Server muestra
--    las CHECK a su manera (por ejemplo, ([Fee]>=(0) AND [Fee]<=(10000))). Antes de aplicar y después
--    de rollback.sql: ninguna fila.
SELECT N'CHECK' AS [Tipo], OBJECT_NAME([parent_object_id]) AS [Tabla], [name] AS [Nombre], NULL AS [Unico],
    [definition] COLLATE DATABASE_DEFAULT AS [Definicion]
FROM sys.check_constraints
WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.DeliveryZones'), OBJECT_ID(N'dbo.DeliveryZonePostalCodes'))
UNION ALL
SELECT N'FK', OBJECT_NAME([parent_object_id]), [name], NULL, [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.DeliveryZones'), OBJECT_ID(N'dbo.DeliveryZonePostalCodes'))
UNION ALL
SELECT N'INDEX', OBJECT_NAME([object_id]), [name], CAST([is_unique] AS int), [filter_definition] COLLATE DATABASE_DEFAULT
FROM sys.indexes
WHERE [object_id] IN (OBJECT_ID(N'dbo.DeliveryZones'), OBJECT_ID(N'dbo.DeliveryZonePostalCodes')) AND [type] > 0
ORDER BY [Tipo], [Tabla], [Nombre];

-- 4. Después de desplegar, y antes de rollback.sql: las zonas guardadas, activas ([Activa] 1) o no,
--    cuántos códigos postales tienen y cuántos negocios las tienen. Con alguna fila, rollback.sql se
--    detiene (error 50012) sin cambiar nada: la bitácora no guarda qué códigos postales tenía cada
--    zona. Recién aplicado: ninguna fila.
IF OBJECT_ID(N'dbo.DeliveryZones') IS NOT NULL
    EXEC(N'SELECT z.[IsActive] AS [Activa], COUNT(DISTINCT z.[Id]) AS [Zonas], COUNT(p.[Id]) AS [CodigosPostales],
    COUNT(DISTINCT z.[TenantId]) AS [Negocios]
FROM [DeliveryZones] z
LEFT JOIN [DeliveryZonePostalCodes] p ON p.[DeliveryZoneId] = z.[Id]
GROUP BY z.[IsActive]
ORDER BY [Activa] DESC;');

-- 5. Después: consistencia. Cada consulta debe dar 0 filas:
--    a. códigos postales cuyo negocio no es el de su zona;
--    b. zonas sin ningún código postal (una zona siempre tiene al menos uno).
IF OBJECT_ID(N'dbo.DeliveryZonePostalCodes') IS NOT NULL
BEGIN
    EXEC(N'SELECT p.[Id], p.[TenantId] AS [NegocioDelCodigo], z.[TenantId] AS [NegocioDeLaZona]
FROM [DeliveryZonePostalCodes] p
INNER JOIN [DeliveryZones] z ON z.[Id] = p.[DeliveryZoneId]
WHERE p.[TenantId] <> z.[TenantId];');
    EXEC(N'SELECT z.[Id], z.[TenantId] AS [Negocio]
FROM [DeliveryZones] z
WHERE NOT EXISTS (SELECT 1 FROM [DeliveryZonePostalCodes] p WHERE p.[DeliveryZoneId] = z.[Id]);');
END;
