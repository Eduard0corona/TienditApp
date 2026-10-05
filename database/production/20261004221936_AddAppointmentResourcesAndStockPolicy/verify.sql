-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004213919_UseMexicanPesosByDefault; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: columnas nuevas. Antes de aplicar no devuelve filas; después, tres (vacío
--    otra vez después de rollback.sql).
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'InsufficientStockPolicy')
   OR (c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] IN (N'ServiceProductId', N'StaffUserId'))
ORDER BY [Tabla], [Columna];

-- 3. Después: el índice de la agenda por empleado (vacío después de rollback.sql).
SELECT i.[name] AS [Indice]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.Appointments') AND i.[name] = N'IX_Appointments_TenantId_StaffUserId_StartAt';

-- 4. Después: política de existencias de cada negocio (justo después de aplicar, todos en 0 =
--    rechazar, como antes) y citas con servicio o empleado. Antes de rollback.sql: los negocios
--    en 1 volverán a rechazar y las citas perderán su servicio y su empleado.
IF COL_LENGTH(N'dbo.TenantSettings', N'InsufficientStockPolicy') IS NOT NULL
    EXEC(N'SELECT [InsufficientStockPolicy], COUNT(*) AS [Negocios]
FROM [TenantSettings]
GROUP BY [InsufficientStockPolicy];
SELECT COUNT([ServiceProductId]) AS [CitasConServicio], COUNT([StaffUserId]) AS [CitasConEmpleado]
FROM [Appointments];');

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004221936_AddAppointmentResourcesAndStockPolicy';
