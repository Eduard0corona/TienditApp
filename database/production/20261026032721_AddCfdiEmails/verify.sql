-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28D), la más reciente es
--    20261024084435_AddCreditNotes. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261026032721_AddCfdiEmails.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla nueva [CfdiEmails], sus restricciones y sus índices, y las columnas
--    [OrderInvoices].[ReceiverEmail] y [Customers].[InvoiceEmail]. Antes de aplicar y después de
--    rollback.sql no hay nada.
SELECT [name] AS [Tabla]
FROM sys.tables
WHERE [name] = N'CfdiEmails';

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] LIKE N'CK[_]CfdiEmails[_]%'
ORDER BY [Tabla], [Restriccion];

SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.CfdiEmails') AND i.[name] IS NOT NULL
ORDER BY [Indice];

SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.OrderInvoices') AND c.[name] = N'ReceiverEmail')
    OR (c.[object_id] = OBJECT_ID(N'dbo.Customers') AND c.[name] = N'InvoiceEmail')
ORDER BY [Tabla];

-- 3. Después: los correos de CFDI por resultado, y las facturas y clientes con correo. rollback.sql se
--    detiene con 50041 si hay alguno. Justo después de aplicar: todo en cero. Antes de aplicar y
--    después de rollback.sql la tabla no existe y la consulta no corre.
IF OBJECT_ID(N'dbo.CfdiEmails') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [CfdiEmails] WHERE [SentAt] IS NULL AND [FailedAt] IS NULL) AS [Enviando],
        (SELECT COUNT(*) FROM [CfdiEmails] WHERE [SentAt] IS NOT NULL) AS [Enviados],
        (SELECT COUNT(*) FROM [CfdiEmails] WHERE [FailedAt] IS NOT NULL) AS [NoSePudieron],
        (SELECT COUNT(*) FROM [OrderInvoices] WHERE [ReceiverEmail] IS NOT NULL) AS [FacturasConCorreo],
        (SELECT COUNT(*) FROM [Customers] WHERE [InvoiceEmail] IS NOT NULL) AS [ClientesConCorreoParaFacturas];');

-- 4. Antes y después: las facturas de pedidos vivas y los clientes con datos fiscales. La migración no
--    los cambia; ninguna factura existente recibe un correo al desplegar (no tienen a quién enviarse
--    hasta que alguien escriba su correo con «Enviar por correo»).
IF OBJECT_ID(N'dbo.OrderInvoices') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [OrderInvoices] WHERE [Status] IN (0, 1, 3)) AS [FacturasVivas],
        (SELECT COUNT(*) FROM [Customers] WHERE [FiscalData_Rfc] IS NOT NULL) AS [ClientesConDatosFiscales];');
