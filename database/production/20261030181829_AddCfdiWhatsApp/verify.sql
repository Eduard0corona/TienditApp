-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28L, sin migración), la
--    más reciente es 20261029163102_AddGlobalInvoiceWriteOffNotes. Si es anterior, revisa primero el
--    verify.sql de su carpeta: apply.sql también aplica las que falten. Después:
--    20261030181829_AddCfdiWhatsApp.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas de [OutgoingMessages], [OrderInvoices] e [InvoicingProfiles], las
--    restricciones e índices nuevos de [OutgoingMessages] y su llave a [OrderInvoices]. Antes de
--    aplicar y después de rollback.sql no hay nada.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.OutgoingMessages')
        AND c.[name] IN (N'CfdiDocument', N'CfdiId', N'OrderInvoiceId', N'RequestedByUserId', N'LinkToken'))
    OR (c.[object_id] = OBJECT_ID(N'dbo.OrderInvoices') AND c.[name] = N'ReceiverWhatsApp')
    OR (c.[object_id] = OBJECT_ID(N'dbo.InvoicingProfiles') AND c.[name] = N'WhatsAppTemplateId')
ORDER BY [Tabla], [Columna];

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] LIKE N'CK[_]OutgoingMessages[_]Cfdi%' OR cc.[name] = N'CK_OutgoingMessages_LinkToken'
ORDER BY [Restriccion];

SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.OutgoingMessages')
    AND i.[name] IN (N'IX_OutgoingMessages_LinkToken', N'IX_OutgoingMessages_OrderInvoiceId',
        N'IX_OutgoingMessages_TenantId_CfdiId_CreatedAt')
ORDER BY [Indice];

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Referencia]
FROM sys.foreign_keys fk
WHERE fk.[name] = N'FK_OutgoingMessages_OrderInvoices_OrderInvoiceId';

-- 3. Después: los mensajes de CFDI de la cola por estado (0 en cola, 1 enviado, 2 no se envió,
--    3 o 4 omitido), y las facturas con WhatsApp y los negocios con plantilla para facturas.
--    rollback.sql se detiene con 50046 si hay alguno. Justo después de aplicar: todo en cero. Antes
--    de aplicar y después de rollback.sql las columnas no existen y la consulta no corre.
IF COL_LENGTH(N'dbo.OutgoingMessages', N'CfdiId') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [OutgoingMessages] WHERE [Purpose] = 5 AND [Status] = 0) AS [EnCola],
        (SELECT COUNT(*) FROM [OutgoingMessages] WHERE [Purpose] = 5 AND [Status] = 1) AS [Enviados],
        (SELECT COUNT(*) FROM [OutgoingMessages] WHERE [Purpose] = 5 AND [Status] = 2) AS [NoSeEnviaron],
        (SELECT COUNT(*) FROM [OutgoingMessages] WHERE [Purpose] = 5 AND [Status] IN (3, 4)) AS [Omitidos],
        (SELECT COUNT(*) FROM [OrderInvoices] WHERE [ReceiverWhatsApp] IS NOT NULL) AS [FacturasConWhatsApp],
        (SELECT COUNT(*) FROM [InvoicingProfiles] WHERE [WhatsAppTemplateId] IS NOT NULL) AS [NegociosConPlantilla];');

-- 4. Antes y después: los mensajes de la cola por propósito y las facturas de pedidos vivas. La
--    migración no los cambia; ningún mensaje ni factura existente recibe datos de CFDI al desplegar.
IF OBJECT_ID(N'dbo.OutgoingMessages') IS NOT NULL
    EXEC(N'SELECT [Purpose] AS [Proposito], COUNT(*) AS [Mensajes]
        FROM [OutgoingMessages] GROUP BY [Purpose] ORDER BY [Purpose];');
IF OBJECT_ID(N'dbo.OrderInvoices') IS NOT NULL
    EXEC(N'SELECT (SELECT COUNT(*) FROM [OrderInvoices] WHERE [Status] IN (0, 1, 3)) AS [FacturasVivas];');
