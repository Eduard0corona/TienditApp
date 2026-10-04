-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004105827_AddTaxPolicyAndCounterSaleClosing; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: tablas nuevas. Antes de aplicar no devuelve filas; después, tres (vacío
--    otra vez después de rollback.sql).
SELECT [name] AS [Tabla]
FROM sys.tables
WHERE [name] IN (N'OutboxMessages', N'OutboxMessageConsumers', N'InboxMessages')
ORDER BY [name];

-- 3. Después, y antes de un rollback: trabajo en las colas. [Pendientes] baja a 0 en segundos
--    con la API en marcha; [Apartados] son los que agotaron sus intentos (revisa los logs). Para
--    revertir, [Pendientes] debe ser 0. EXEC evita que la consulta falle sin las tablas.
IF OBJECT_ID(N'dbo.OutboxMessages') IS NOT NULL
    EXEC(N'SELECT N''Eventos (outbox)'' AS [Cola],
    SUM(CASE WHEN [ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL THEN 1 ELSE 0 END) AS [Pendientes],
    SUM(CASE WHEN [DeadLetteredAt] IS NOT NULL THEN 1 ELSE 0 END) AS [Apartados],
    COUNT(*) AS [Total]
FROM [OutboxMessages]
UNION ALL
SELECT N''Webhook (inbox)'',
    SUM(CASE WHEN [ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL THEN 1 ELSE 0 END),
    SUM(CASE WHEN [DeadLetteredAt] IS NOT NULL THEN 1 ELSE 0 END),
    COUNT(*)
FROM [InboxMessages];');

-- 4. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox';
