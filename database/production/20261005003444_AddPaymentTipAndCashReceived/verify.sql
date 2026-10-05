-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261005003434_AddFolioSequences; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: columnas nuevas. Antes de aplicar no devuelve filas; después, dos (vacío
--    otra vez después de rollback.sql).
SELECT c.[name] AS [Columna]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.OrderPayments') AND c.[name] IN (N'Tip', N'CashReceived')
ORDER BY c.[name];

-- 3. Después: pagos con propina y con efectivo recibido. Justo después de aplicar, ambos en 0.
--    Antes de rollback.sql, [PagosConPropina] debe ser 0 (si no, el script se detiene sin cambiar
--    nada, error 50006).
IF COL_LENGTH(N'dbo.OrderPayments', N'Tip') IS NOT NULL
    EXEC(N'SELECT
    COUNT(CASE WHEN [Tip] <> 0 THEN 1 END) AS [PagosConPropina],
    COUNT([CashReceived]) AS [PagosConEfectivoRecibido]
FROM [OrderPayments];');

-- 4. Después: migraciones de la Fase 4. Deben aparecer las ocho.
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] IN (
    N'20261004203922_AddOrderNotificationPolicy',
    N'20261004203947_AlignTenantAdminMemberships',
    N'20261004213919_UseMexicanPesosByDefault',
    N'20261004221936_AddAppointmentResourcesAndStockPolicy',
    N'20261004224714_AddCustomerAddressesAndChatPause',
    N'20261004232541_AddChargePermissionAndSmsCooldown',
    N'20261005003434_AddFolioSequences',
    N'20261005003444_AddPaymentTipAndCashReceived')
ORDER BY [MigrationId];
