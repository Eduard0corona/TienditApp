-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004160303_AddBranchesAndDailySalesSnapshots; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: tablas nuevas. Antes de aplicar no devuelve filas; después, tres (vacío
--    otra vez después de rollback.sql).
SELECT t.[name] AS [Tabla]
FROM sys.tables t
WHERE t.[name] IN (N'Plans', N'TenantSubscriptions', N'SubscriptionPayments')
ORDER BY t.[name];

-- 3. Después: planes, suscripciones y pagos. Justo después de aplicar, todo en 0: ningún negocio
--    tiene plan ni límites hasta que la plataforma se lo asigne.
IF OBJECT_ID(N'dbo.Plans') IS NOT NULL
    EXEC(N'SELECT
    (SELECT COUNT(*) FROM [Plans]) AS [Planes],
    (SELECT COUNT(*) FROM [TenantSubscriptions]) AS [Suscripciones],
    (SELECT COUNT(*) FROM [SubscriptionPayments]) AS [Pagos];');

-- 4. Antes de rollback.sql: [Pagos] debe ser 0 (si no, el script se detiene sin cambiar nada,
--    error 50004). Los negocios listados quedaron suspendidos (2) o cancelados (1) por la
--    facturación: reactívalos antes de revertir, porque sin suscripciones nadie lo haría.
IF OBJECT_ID(N'dbo.Plans') IS NOT NULL
    EXEC(N'SELECT (SELECT COUNT(*) FROM [SubscriptionPayments]) AS [Pagos];
SELECT t.[Name] AS [Negocio], t.[Status] AS [EstadoNegocio], s.[Status] AS [EstadoSuscripcion], s.[StatusReason] AS [Motivo]
FROM [TenantSubscriptions] s
INNER JOIN [Tenants] t ON t.[Id] = s.[TenantId]
WHERE t.[Status] <> 0
ORDER BY t.[Name];');

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions';
