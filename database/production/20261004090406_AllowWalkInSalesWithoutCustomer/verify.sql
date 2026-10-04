-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Si la más reciente es anterior a
--    20261002094722_AddWhatsAppConversationVersion, el script también aplicará las que
--    falten (revisa primero el verify.sql de esa carpeta).
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: órdenes por tipo de referencia al cliente. Antes de aplicar, las
--    columnas [SinCliente] y [Ambas] deben ser 0. Después, [SinCliente] crece con las
--    ventas de mostrador sin teléfono; [Ambas] y [ClienteSinReferencia] siempre deben ser 0.
SELECT
    SUM(CASE WHEN [CustomerUserId] IS NOT NULL AND [TemporaryCustomerId] IS NULL THEN 1 ELSE 0 END) AS [ClienteRegistrado],
    SUM(CASE WHEN [CustomerUserId] IS NULL AND [TemporaryCustomerId] IS NOT NULL THEN 1 ELSE 0 END) AS [ClienteTemporal],
    SUM(CASE WHEN [CustomerUserId] IS NULL AND [TemporaryCustomerId] IS NULL THEN 1 ELSE 0 END) AS [SinCliente],
    SUM(CASE WHEN [CustomerUserId] IS NOT NULL AND [TemporaryCustomerId] IS NOT NULL THEN 1 ELSE 0 END) AS [Ambas],
    SUM(CASE WHEN [CustomerUserId] IS NULL AND [TemporaryCustomerId] IS NULL AND [CreatedByBusiness] = 0 THEN 1 ELSE 0 END) AS [ClienteSinReferencia]
FROM [Orders];

-- 3. Después: definición vigente de la restricción. Con apply.sql debe incluir
--    [CreatedByBusiness]=(1) y is_not_trusted = 0; después de rollback.sql vuelve la regla
--    anterior y is_not_trusted = 1 si ya había ventas sin cliente (WITH NOCHECK).
SELECT cc.[name], cc.[definition], cc.[is_not_trusted], cc.[is_disabled]
FROM sys.check_constraints cc
WHERE cc.[parent_object_id] = OBJECT_ID(N'dbo.Orders') AND cc.[name] = N'CK_Orders_CustomerLink';

-- 4. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004090406_AllowWalkInSalesWithoutCustomer';
