-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql. No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Si la más reciente es anterior a
--    20261001155915_AddWhatsAppWebhookUniqueIndexes, el script también aplicará las que falten.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes, solo si falta 20261001155915_AddWhatsAppWebhookUniqueIndexes: duplicados que
--    harían fallar sus índices únicos. Deben salir vacías. Si no, el script se revierte
--    completo, y resolverlos es una decisión de negocio: no se borran automáticamente.
SELECT [PhoneNumberId], COUNT(*) AS [Total]
FROM [WhatsAppConfigs]
WHERE [PhoneNumberId] IS NOT NULL AND LTRIM(RTRIM([PhoneNumberId])) <> ''
GROUP BY [PhoneNumberId]
HAVING COUNT(*) > 1;

SELECT [ExternalMessageId], COUNT(*) AS [Total]
FROM [WhatsAppMessages]
WHERE [ExternalMessageId] IS NOT NULL
GROUP BY [ExternalMessageId]
HAVING COUNT(*) > 1;

-- 3. Antes: filas de la tabla que cambia (agregar [Version] escribe en cada una).
SELECT COUNT(*) AS [Conversaciones] FROM [WhatsAppConversations];

-- 4. Después: la columna es rowversion NOT NULL ("timestamp" en sys.types) y la migración
--    quedó registrada.
SELECT TYPE_NAME(c.[user_type_id]) AS [Tipo], c.[is_nullable] AS [AdmiteNull]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.WhatsAppConversations') AND c.[name] = N'Version';

SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261002094722_AddWhatsAppConversationVersion';
