-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas; nunca
-- muestran la llave de los códigos.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28E), la más reciente es
--    20261026032721_AddCfdiEmails. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261026082041_AddSelfInvoicing.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas [InvoicingProfiles].[SelfInvoicing] (bit, no nula, 0 por omisión) y
--    [InvoicingProfiles].[SelfInvoiceKey] (varbinary(32), nula), y la restricción
--    [CK_InvoicingProfiles_SelfInvoicing]. Antes de aplicar y después de rollback.sql no hay nada.
SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [Nulo],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.InvoicingProfiles') AND c.[name] IN (N'SelfInvoicing', N'SelfInvoiceKey')
ORDER BY [Columna];

SELECT
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_InvoicingProfiles_SelfInvoicing';

-- 3. Después: los negocios con la autofactura activa y los que ya tienen llave (activa o apagada).
--    rollback.sql se detiene con 50042 si alguno tiene llave. Justo después de aplicar: todo en cero.
--    Antes de aplicar y después de rollback.sql las columnas no existen y la consulta no corre.
IF COL_LENGTH(N'dbo.InvoicingProfiles', N'SelfInvoiceKey') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [InvoicingProfiles] WHERE [SelfInvoicing] = 1) AS [ConAutofactura],
        (SELECT COUNT(*) FROM [InvoicingProfiles] WHERE [SelfInvoiceKey] IS NOT NULL) AS [ConLlave];');

-- 4. Antes y después: los perfiles de facturación, las facturas de pedidos vivas y las que pidieron
--    los clientes en línea (quién la pidió: 00000000-0000-0000-0000-00000000028f). La migración no
--    los cambia; justo después de aplicar no hay ninguna pedida en línea.
IF OBJECT_ID(N'dbo.OrderInvoices') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [InvoicingProfiles]) AS [PerfilesDeFacturacion],
        (SELECT COUNT(*) FROM [OrderInvoices] WHERE [Status] IN (0, 1, 3)) AS [FacturasVivas],
        (SELECT COUNT(*) FROM [OrderInvoices]
            WHERE [RequestedByUserId] = ''00000000-0000-0000-0000-00000000028f'') AS [PedidasEnLinea];');
