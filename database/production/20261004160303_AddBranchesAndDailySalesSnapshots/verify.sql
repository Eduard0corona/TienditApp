-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004153003_AddOnlineMenu; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes: ubicaciones de inventario por negocio. Cada una se volverá una sucursal; un negocio
--    sin ubicaciones recibirá "Sucursal principal".
SELECT t.[Name] AS [Negocio], COUNT(l.[Id]) AS [Ubicaciones],
    SUM(CASE WHEN l.[IsActive] = 1 THEN 1 ELSE 0 END) AS [Activas],
    SUM(CASE WHEN l.[IsDefault] = 1 THEN 1 ELSE 0 END) AS [Predeterminadas]
FROM [Tenants] t
LEFT JOIN [InventoryLocations] l ON l.[TenantId] = t.[Id]
GROUP BY t.[Id], t.[Name]
ORDER BY t.[Name];

-- 3. Después: todo quedó en una sucursal. [Sucursales] = ubicaciones más los negocios que no
--    tenían; las otras columnas deben ser 0.
IF OBJECT_ID(N'dbo.Branches') IS NOT NULL
    EXEC(N'SELECT
    (SELECT COUNT(*) FROM [Tenants]) AS [Negocios],
    (SELECT COUNT(*) FROM [Branches]) AS [Sucursales],
    (SELECT COUNT(*) FROM [Tenants] t
        WHERE NOT EXISTS (SELECT 1 FROM [Branches] b WHERE b.[TenantId] = t.[Id] AND b.[IsDefault] = 1)) AS [NegociosSinPredeterminada],
    (SELECT COUNT(*) FROM [Orders] WHERE [BranchId] IS NULL) AS [OrdenesSinSucursal],
    (SELECT COUNT(*) FROM [CashSessions] WHERE [BranchId] IS NULL) AS [CajasSinSucursal],
    (SELECT COUNT(*) FROM [InventoryLocations] WHERE [BranchId] IS NULL) AS [UbicacionesSinSucursal];');

-- 4. Después: sucursales de cada negocio con sus órdenes y cajas.
IF OBJECT_ID(N'dbo.Branches') IS NOT NULL
    EXEC(N'SELECT t.[Name] AS [Negocio], b.[Name] AS [Sucursal], b.[Code] AS [Clave],
    b.[IsDefault] AS [Predeterminada], b.[IsActive] AS [Activa],
    (SELECT COUNT(*) FROM [Orders] o WHERE o.[BranchId] = b.[Id]) AS [Ordenes],
    (SELECT COUNT(*) FROM [CashSessions] s WHERE s.[BranchId] = b.[Id]) AS [Cajas]
FROM [Branches] b
INNER JOIN [Tenants] t ON t.[Id] = b.[TenantId]
ORDER BY t.[Name], b.[IsDefault] DESC, b.[Name];');

-- 5. Después (limpieza): sucursales activas sin órdenes ni cajas, casi siempre ubicaciones que
--    solo eran almacén. Si no venden, desactívalas en Configuración > Sucursales para que no
--    aparezcan al elegir dónde se vende (la predeterminada no se lista).
IF OBJECT_ID(N'dbo.Branches') IS NOT NULL
    EXEC(N'SELECT t.[Name] AS [Negocio], b.[Name] AS [Sucursal], b.[Code] AS [Clave]
FROM [Branches] b
INNER JOIN [Tenants] t ON t.[Id] = b.[TenantId]
WHERE b.[IsActive] = 1 AND b.[IsDefault] = 0
  AND NOT EXISTS (SELECT 1 FROM [Orders] o WHERE o.[BranchId] = b.[Id])
  AND NOT EXISTS (SELECT 1 FROM [CashSessions] s WHERE s.[BranchId] = b.[Id])
ORDER BY t.[Name], b.[Name];');

-- 6. Antes de rollback.sql: debe estar vacío. Son sucursales que no venían de una ubicación
--    (creadas después, o la "Sucursal principal" ya editada); si hay, rollback.sql se detiene sin
--    cambiar nada (error 50003). Resuélvelas a mano antes de revertir.
IF OBJECT_ID(N'dbo.Branches') IS NOT NULL
    EXEC(N'SELECT t.[Name] AS [Negocio], b.[Name] AS [Sucursal], b.[Code] AS [Clave], b.[CreatedAt] AS [Creada]
FROM [Branches] b
INNER JOIN [Tenants] t ON t.[Id] = b.[TenantId]
WHERE NOT EXISTS (SELECT 1 FROM [InventoryLocations] l WHERE l.[Id] = b.[Id])
  AND NOT (b.[IsDefault] = 1 AND b.[Code] = N''PRINCIPAL'' AND b.[Name] = N''Sucursal principal''
      AND b.[Description] IS NULL AND b.[AddressLine1] IS NULL AND b.[AddressLine2] IS NULL AND b.[City] IS NULL
      AND b.[State] IS NULL AND b.[Country] IS NULL AND b.[PostalCode] IS NULL
      AND b.[ContactName] IS NULL AND b.[ContactPhone] IS NULL)
ORDER BY t.[Name], b.[Name];');

-- 7. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots';
