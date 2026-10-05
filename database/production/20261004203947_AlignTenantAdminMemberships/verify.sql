-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004203922_AddOrderNotificationPolicy; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes: membresías que la migración cambia a administrador (cuentas TENANT_ADMIN cuya única
--    membresía dice personal). Después de aplicar, vacío.
SELECT tu.[UserId] AS [Usuario], t.[Name] AS [Negocio]
FROM [TenantUsers] tu
INNER JOIN [AspNetUsers] u ON u.[Id] = tu.[UserId]
INNER JOIN [Tenants] t ON t.[Id] = tu.[TenantId]
WHERE u.[Role] = 1
  AND tu.[Role] = 2
  AND NOT EXISTS (SELECT 1 FROM [TenantUsers] other WHERE other.[UserId] = tu.[UserId] AND other.[Id] <> tu.[Id])
ORDER BY t.[Name];

-- 3. Después: cuentas TENANT_ADMIN que en algún negocio son personal. Están en varios negocios y
--    la migración no las toca: desde la Fase 4, en ese negocio ya no administran. Si alguna debe
--    administrarlo, cámbiala en Equipo.
SELECT tu.[UserId] AS [Usuario], t.[Name] AS [Negocio]
FROM [TenantUsers] tu
INNER JOIN [AspNetUsers] u ON u.[Id] = tu.[UserId]
INNER JOIN [Tenants] t ON t.[Id] = tu.[TenantId]
WHERE u.[Role] = 1 AND tu.[Role] = 2
ORDER BY t.[Name];

-- 4. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004203947_AlignTenantAdminMemberships';
