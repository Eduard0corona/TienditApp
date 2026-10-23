-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24K), la más reciente es
--    20261022213253_AddOnlinePaymentReconciliation. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261023064133_AddOrderInvoicing.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las tablas nuevas [OrderInvoices] e [InvoicingProfiles], las columnas
--    [Customers].[FiscalData_*] (con nulos), sus restricciones y sus índices. Antes de aplicar y
--    después de rollback.sql: nada de esto.
SELECT [name] AS [Tabla]
FROM sys.tables
WHERE [name] IN (N'OrderInvoices', N'InvoicingProfiles')
ORDER BY [name];

SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [Nulos]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.Customers') AND c.[name] LIKE N'FiscalData[_]%'
ORDER BY [Columna];

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_Customers_FiscalData' OR cc.[name] LIKE N'CK[_]OrderInvoices[_]%'
    OR cc.[name] LIKE N'CK[_]InvoicingProfiles[_]%'
ORDER BY [Tabla], [Restriccion];

SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] IN (OBJECT_ID(N'dbo.OrderInvoices'), OBJECT_ID(N'dbo.InvoicingProfiles')) AND i.[name] IS NOT NULL
ORDER BY [Tabla], [Indice];

-- 3. Antes y después de aplicar, igual: los clientes por negocio (la migración no los cambia).
SELECT COUNT(*) AS [Clientes], SUM(CASE WHEN [IsPublicGeneral] = 1 THEN 1 ELSE 0 END) AS [PublicoEnGeneral]
FROM [Customers];

-- 4. Después: las facturas por estado, los CSD registrados y los clientes con datos fiscales.
--    rollback.sql se detiene con 50037 si alguno no es cero. Justo después de aplicar: todos en cero.
--    Antes de aplicar y después de rollback.sql las tablas no existen y la consulta no corre.
IF OBJECT_ID(N'dbo.OrderInvoices') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [OrderInvoices] WHERE [Status] = 0) AS [Timbrando],
        (SELECT COUNT(*) FROM [OrderInvoices] WHERE [Status] = 1) AS [Timbradas],
        (SELECT COUNT(*) FROM [OrderInvoices] WHERE [Status] = 2) AS [Rechazadas],
        (SELECT COUNT(*) FROM [OrderInvoices] WHERE [Status] = 3) AS [Cancelandose],
        (SELECT COUNT(*) FROM [OrderInvoices] WHERE [Status] = 4) AS [Canceladas],
        (SELECT COUNT(*) FROM [InvoicingProfiles] WHERE [CertificateNumber] IS NOT NULL) AS [CsdRegistrados],
        (SELECT COUNT(*) FROM [Customers] WHERE [FiscalData_Rfc] IS NOT NULL) AS [ClientesConDatosFiscales];');
