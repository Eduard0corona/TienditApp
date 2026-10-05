-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004221936_AddAppointmentResourcesAndStockPolicy; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: columnas nuevas. Antes de aplicar no devuelve filas; después, dos (vacío
--    otra vez después de rollback.sql).
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.WhatsAppConversations') AND c.[name] = N'AutoRepliesPausedUntil')
   OR (c.[object_id] = OBJECT_ID(N'dbo.Customers') AND c.[name] = N'Notes')
ORDER BY [Tabla], [Columna];

-- 3. Después: la tabla de direcciones (vacío después de rollback.sql).
SELECT [name] AS [Tabla]
FROM sys.tables
WHERE [object_id] = OBJECT_ID(N'dbo.CustomerAddresses');

-- 4. Antes de rollback.sql: lo que se perdería (direcciones, notas de clientes y conversaciones
--    con las respuestas automáticas en pausa).
IF OBJECT_ID(N'dbo.CustomerAddresses') IS NOT NULL
    EXEC(N'SELECT
    (SELECT COUNT(*) FROM [CustomerAddresses]) AS [Direcciones],
    (SELECT COUNT(*) FROM [Customers] WHERE [Notes] IS NOT NULL) AS [ClientesConNotas],
    (SELECT COUNT(*) FROM [WhatsAppConversations] WHERE [AutoRepliesPausedUntil] IS NOT NULL) AS [ConversacionesEnPausa];');

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause';
