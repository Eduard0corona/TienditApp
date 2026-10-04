-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004131615_AddMessagingCampaignsAndReminders; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: columna nueva. Antes de aplicar no devuelve filas; después, una (vacío
--    otra vez después de rollback.sql).
SELECT c.[name] AS [Columna]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.Orders') AND c.[name] = N'Channel';

-- 3. Después: órdenes por canal (0 = Mostrador, 1 = WhatsApp, 2 = En línea). Justo después de
--    aplicar, todas en 0.
IF COL_LENGTH(N'dbo.Orders', N'Channel') IS NOT NULL
    EXEC(N'SELECT [Channel], COUNT(*) AS [Ordenes] FROM [Orders] GROUP BY [Channel] ORDER BY [Channel];');

-- 4. Después: migraciones de las Fases 1 y 2. Deben aparecer las seis.
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] IN (
    N'20261004105827_AddTaxPolicyAndCounterSaleClosing',
    N'20261004121539_AddOutboxAndWebhookInbox',
    N'20261004123654_CashSessionsPerCashier',
    N'20261004125133_AddOrderRefunds',
    N'20261004131615_AddMessagingCampaignsAndReminders',
    N'20261004133416_AddOrderChannel')
ORDER BY [MigrationId];
