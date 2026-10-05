-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004165450_AddOfflineSaleIdempotency; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: la columna nueva. Antes de aplicar no devuelve filas; después, una (vacío
--    otra vez después de rollback.sql).
SELECT c.[name] AS [Columna]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'OrderNotificationPolicy';

-- 3. Antes: negocios con ventas de mostrador con teléfono en los últimos 30 días. Hasta ahora esos
--    clientes recibían los avisos de WhatsApp de cada estado; con la política por defecto (0) ya
--    no. Si un negocio los quiere, elige "todas las órdenes" en Mi negocio.
SELECT t.[Name] AS [Negocio], COUNT(*) AS [VentasDeMostradorConTelefono]
FROM [Orders] o
INNER JOIN [Tenants] t ON t.[Id] = o.[TenantId]
WHERE o.[Channel] = 0
  AND o.[CustomerPhone] IS NOT NULL
  AND o.[CreatedAt] >= DATEADD(DAY, -30, SYSDATETIMEOFFSET())
GROUP BY t.[Name]
ORDER BY [VentasDeMostradorConTelefono] DESC;

-- 4. Después: política de cada negocio. Justo después de aplicar, todos en 0 (pedidos a distancia
--    y servicios).
IF COL_LENGTH(N'dbo.TenantSettings', N'OrderNotificationPolicy') IS NOT NULL
    EXEC(N'SELECT [OrderNotificationPolicy], COUNT(*) AS [Negocios]
FROM [TenantSettings]
GROUP BY [OrderNotificationPolicy];');

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004203922_AddOrderNotificationPolicy';
