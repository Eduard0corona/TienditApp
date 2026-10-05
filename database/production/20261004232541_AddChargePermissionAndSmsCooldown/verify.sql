-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004224714_AddCustomerAddressesAndChatPause; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes: membresías que reciben el permiso de cobrar (permisos guardados con ManageOrders, sin
--    ChargeOrders, de cuentas que no son de cocina). Después de aplicar, 0.
SELECT COUNT(*) AS [MembresiasQueRecibenCobrar]
FROM [TenantUsers] tu
INNER JOIN [AspNetUsers] u ON u.[Id] = tu.[UserId]
WHERE tu.[PermissionCodes] IS NOT NULL
  AND (N',' + tu.[PermissionCodes] + N',') LIKE N'%,ManageOrders,%'
  AND (N',' + tu.[PermissionCodes] + N',') NOT LIKE N'%,ChargeOrders,%'
  AND u.[Role] <> 3;

-- 3. Después: membresías que toman pedidos y no cobran (cuentas de cocina, o a quien se le quitó
--    después en Equipo).
SELECT COUNT(*) AS [TomanPedidosSinCobrar]
FROM [TenantUsers] tu
WHERE tu.[PermissionCodes] IS NOT NULL
  AND (N',' + tu.[PermissionCodes] + N',') LIKE N'%,ManageOrders,%'
  AND (N',' + tu.[PermissionCodes] + N',') NOT LIKE N'%,ChargeOrders,%';

-- 4. Antes y después: la columna nueva (vacío antes de aplicar y después de rollback.sql).
SELECT c.[name] AS [Columna]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.AspNetUsers') AND c.[name] = N'SmsCodeSentAt';

-- 5. Antes: los permisos más largos guardados. La columna admite 1000 caracteres y la migración
--    agrega 13 (",ChargeOrders"): debe ser menor que 988.
SELECT MAX(LEN([PermissionCodes])) AS [LargoMaximo]
FROM [TenantUsers];

-- 6. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004232541_AddChargePermissionAndSmsCooldown';
