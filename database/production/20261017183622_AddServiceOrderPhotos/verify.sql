-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24C), la más reciente es
--    20261017100104_AddOrderTrackingToken. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261017183622_AddServiceOrderPhotos.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla [ServiceOrderPhotos] con sus 10 columnas, la restricción
--    CK_ServiceOrderPhotos_Stage, la llave a [Orders] con borrado en cascada y sus dos índices.
--    Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT N'TABLE' AS [Tipo], t.[name] AS [Nombre], CAST(COUNT(c.[column_id]) AS nvarchar(20)) AS [Definicion]
FROM sys.tables t
INNER JOIN sys.columns c ON c.[object_id] = t.[object_id]
WHERE t.[object_id] = OBJECT_ID(N'dbo.ServiceOrderPhotos')
GROUP BY t.[name]
UNION ALL
SELECT N'CHECK', [name], [definition] COLLATE DATABASE_DEFAULT
FROM sys.check_constraints
WHERE [parent_object_id] = OBJECT_ID(N'dbo.ServiceOrderPhotos')
UNION ALL
SELECT N'FOREIGN KEY', [name], [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [parent_object_id] = OBJECT_ID(N'dbo.ServiceOrderPhotos')
UNION ALL
SELECT N'INDEX', [name], NULL
FROM sys.indexes
WHERE [object_id] = OBJECT_ID(N'dbo.ServiceOrderPhotos') AND [name] LIKE N'IX[_]%'
ORDER BY [Tipo], [Nombre];

-- 3. Después de desplegar, y antes de rollback.sql: cuántas fotos se agregaron, de cuántas órdenes
--    y de cuántos negocios, por etapa (0 antes, 1 después). Recién aplicado: ninguna fila. Con una
--    sola, rollback.sql se detiene (THROW 50018).
IF OBJECT_ID(N'dbo.ServiceOrderPhotos') IS NOT NULL
    EXEC(N'SELECT [Stage] AS [Etapa], COUNT(*) AS [Fotos], COUNT(DISTINCT [OrderId]) AS [Ordenes],
    COUNT(DISTINCT [TenantId]) AS [Negocios]
FROM [ServiceOrderPhotos]
GROUP BY [Stage]
ORDER BY [Stage];');

-- 4. Después: consistencia. Cada consulta debe dar 0 filas:
--    a. fotos de una orden que no es de servicio ([OrderType] 1 es Service);
--    b. fotos cuyo negocio no es el de su orden;
--    c. fotos que no apuntan a un archivo privado de su negocio (/files/orders/{negocio}/...).
IF OBJECT_ID(N'dbo.ServiceOrderPhotos') IS NOT NULL
BEGIN
    EXEC(N'SELECT p.[Id], p.[OrderId], o.[OrderType]
FROM [ServiceOrderPhotos] p
INNER JOIN [Orders] o ON o.[Id] = p.[OrderId]
WHERE o.[OrderType] <> 1;');
    EXEC(N'SELECT p.[Id], p.[OrderId], p.[TenantId], o.[TenantId] AS [TenantDeLaOrden]
FROM [ServiceOrderPhotos] p
INNER JOIN [Orders] o ON o.[Id] = p.[OrderId]
WHERE p.[TenantId] <> o.[TenantId];');
    EXEC(N'SELECT [Id], [OrderId], [TenantId]
FROM [ServiceOrderPhotos]
WHERE [FileUrl] NOT LIKE N''/files/orders/'' + LOWER(CONVERT(nvarchar(36), [TenantId])) + N''/%'';');
END;
