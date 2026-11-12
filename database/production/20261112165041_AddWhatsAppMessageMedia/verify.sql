-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 31I), la más reciente es
--    20261111060433_AddOnlineBookingStaffAbsencesAndPhotos. Si es anterior, revisa primero el verify.sql
--    de su carpeta: apply.sql también aplica las que falten. Después: 20261112154235_AddAccountCartsAndPostalCode,
--    20261112161350_AddOnlineOrderReminderAndReviewEmails y 20261112165041_AddWhatsAppMessageMedia.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas (todas nulas): [AspNetUsers].[PostalCode] (varchar 5),
--    [Orders].[ConfirmReminderSentAt] (datetimeoffset), [TenantUsers].[ReviewEmails] (bit) y
--    [WhatsAppMessages].[MediaKind], [MediaId], [MediaPath] y [MediaState]; la tabla [AccountCarts] con
--    sus columnas, sus llaves (a [AspNetUsers], se borra con la cuenta; a [Tenants], sin cascada) y sus
--    índices (uno único por cuenta y tienda); y la restricción [CK_WhatsAppMessages_Media]. Antes de
--    aplicar y después de rollback.sql no existen y las consultas no devuelven filas.
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo], c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.AspNetUsers') AND c.[name] = N'PostalCode')
    OR (c.[object_id] = OBJECT_ID(N'dbo.Orders') AND c.[name] = N'ConfirmReminderSentAt')
    OR (c.[object_id] = OBJECT_ID(N'dbo.TenantUsers') AND c.[name] = N'ReviewEmails')
    OR (c.[object_id] = OBJECT_ID(N'dbo.WhatsAppMessages') AND c.[name] IN (N'MediaKind', N'MediaId', N'MediaPath', N'MediaState'))
    OR c.[object_id] = OBJECT_ID(N'dbo.AccountCarts')
ORDER BY [Tabla], c.[column_id];

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Tabla], fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] = OBJECT_ID(N'dbo.AccountCarts')
ORDER BY fk.[name];

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.AccountCarts') AND i.[type] > 0
ORDER BY i.[name];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_WhatsAppMessages_Media';

-- 3. Antes de rollback.sql (detiene la 31J): cuántos carritos guardaron las cuentas de cliente
--    (CarritosVaciados: de ellos, los que ya se pidieron o vaciaron; quedan vacíos una semana para que
--    otro dispositivo no los vuelva a llenar) y cuántas guardaron su código postal. Justo después de
--    aplicar son 0. rollback.sql se detiene con el error 50060, sin cambiar nada de la 31J, mientras
--    Carritos o CodigosPostales no sea 0: se perderían.
--    Para revertir de todos modos, guarda esta salida y solo entonces bórralos:
--        DELETE FROM [AccountCarts];
--        UPDATE [AspNetUsers] SET [PostalCode] = NULL WHERE [PostalCode] IS NOT NULL;
--    Antes de aplicar y después de rollback.sql la tabla no existe y esta parte no corre.
IF OBJECT_ID(N'dbo.AccountCarts', N'U') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [AccountCarts]) AS [Carritos],
        (SELECT COUNT(*) FROM [AccountCarts] WHERE [Lines] = N''[]'') AS [CarritosVaciados],
        (SELECT COUNT(DISTINCT [UserId]) FROM [AccountCarts]) AS [CuentasConCarrito],
        (SELECT COUNT(*) FROM [AspNetUsers] WHERE [PostalCode] IS NOT NULL) AS [CodigosPostales];');

-- 4. Antes de rollback.sql (detiene la 31L): por negocio, cuántas fotos y audios de sus clientes por
--    WhatsApp ya se guardaron. Justo después de aplicar no sale ninguna fila. rollback.sql se detiene
--    con el error 50061, sin cambiar nada, mientras salga alguna: se perdería dónde están. Para
--    revertir de todos modos, guarda esta salida y solo entonces déjalos como marcador:
--        UPDATE [WhatsAppMessages] SET [MediaPath] = NULL, [MediaState] = 2 WHERE [MediaPath] IS NOT NULL;
--    Los archivos se quedan en el almacenamiento privado (whatsapp/<negocio>). Antes de aplicar y
--    después de rollback.sql la columna no existe y esta parte no corre.
IF COL_LENGTH(N'dbo.WhatsAppMessages', N'MediaPath') IS NOT NULL
    EXEC(N'SELECT t.[Slug] AS [Negocio],
        SUM(CASE WHEN m.[MediaKind] = 1 THEN 1 ELSE 0 END) AS [Fotos],
        SUM(CASE WHEN m.[MediaKind] = 2 THEN 1 ELSE 0 END) AS [Audios]
    FROM [WhatsAppMessages] m
    JOIN [WhatsAppConversations] c ON c.[Id] = m.[ConversationId]
    JOIN [Tenants] t ON t.[Id] = c.[TenantId]
    WHERE m.[MediaPath] IS NOT NULL
    GROUP BY t.[Slug]
    ORDER BY t.[Slug];');

-- 5. Antes y después: cuántos pedidos, miembros y mensajes de WhatsApp hay. La migración no cambia
--    filas: solo cambian por lo que pase entre las dos corridas. Después también: cuántos pedidos se
--    recordaron, cuántos miembros eligieron los correos de reseñas (encendidos o apagados) y en qué
--    van los medios de WhatsApp (0 pendiente, 1 guardado, 2 no disponible); justo después de aplicar,
--    todo en 0.
SELECT (SELECT COUNT(*) FROM [Orders]) AS [Pedidos],
    (SELECT COUNT(*) FROM [TenantUsers]) AS [Miembros],
    (SELECT COUNT(*) FROM [WhatsAppMessages]) AS [MensajesWhatsApp];

IF COL_LENGTH(N'dbo.Orders', N'ConfirmReminderSentAt') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [Orders] WHERE [ConfirmReminderSentAt] IS NOT NULL) AS [PedidosRecordados],
        (SELECT COUNT(*) FROM [TenantUsers] WHERE [ReviewEmails] = 1) AS [ResenasEncendidas],
        (SELECT COUNT(*) FROM [TenantUsers] WHERE [ReviewEmails] = 0) AS [ResenasApagadas];');

IF COL_LENGTH(N'dbo.WhatsAppMessages', N'MediaState') IS NOT NULL
    EXEC(N'SELECT [MediaState] AS [Estado], COUNT(*) AS [Mensajes]
    FROM [WhatsAppMessages]
    WHERE [MediaState] IS NOT NULL
    GROUP BY [MediaState]
    ORDER BY [MediaState];');
