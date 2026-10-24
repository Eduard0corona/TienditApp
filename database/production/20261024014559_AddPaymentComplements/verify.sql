-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28B), la más reciente es
--    20261023175930_AddGlobalInvoices. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261024014559_AddPaymentComplements.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla nueva [PaymentComplements], sus restricciones y sus índices, la columna
--    [InvoicingProfiles].[PaymentSerie] y la restricción de método de pago de las facturas (antes y
--    después de rollback.sql: "[PaymentMethod]='PUE'"; después de aplicar: PUE o PPD con la forma 99).
SELECT [name] AS [Tabla]
FROM sys.tables
WHERE [name] = N'PaymentComplements';

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] LIKE N'CK[_]PaymentComplements[_]%' OR cc.[name] = N'CK_OrderInvoices_PaymentMethod'
ORDER BY [Tabla], [Restriccion];

SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.PaymentComplements') AND i.[name] IS NOT NULL
ORDER BY [Indice];

SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [Nulo],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.InvoicingProfiles') AND c.[name] = N'PaymentSerie';

-- 3. Después: los complementos de pago por estado. rollback.sql se detiene con 50039 si hay alguno.
--    Justo después de aplicar: todo en cero. Antes de aplicar y después de rollback.sql la tabla no
--    existe y la consulta no corre.
IF OBJECT_ID(N'dbo.PaymentComplements') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [PaymentComplements] WHERE [Status] = 0) AS [Timbrando],
        (SELECT COUNT(*) FROM [PaymentComplements] WHERE [Status] = 1) AS [Timbrados],
        (SELECT COUNT(*) FROM [PaymentComplements] WHERE [Status] = 2) AS [Rechazados],
        (SELECT COUNT(*) FROM [PaymentComplements] WHERE [Status] = 3) AS [Cancelandose],
        (SELECT COUNT(*) FROM [PaymentComplements] WHERE [Status] = 4) AS [Cancelados];');

-- 4. Antes y después: las facturas de pedidos por método y estado. Antes de aplicar y justo después,
--    todas PUE (la migración no las cambia); rollback.sql se detiene con 50039 si hay alguna PPD.
IF OBJECT_ID(N'dbo.OrderInvoices') IS NOT NULL
    EXEC(N'SELECT [PaymentMethod], [Status], COUNT(*) AS [Facturas]
        FROM [OrderInvoices]
        GROUP BY [PaymentMethod], [Status]
        ORDER BY [PaymentMethod], [Status];');

-- 5. Después: la serie de los complementos de cada perfil de facturación (todas «P» justo después de
--    aplicar). Antes de aplicar y después de rollback.sql la columna no existe y la consulta no corre.
IF COL_LENGTH(N'dbo.InvoicingProfiles', N'PaymentSerie') IS NOT NULL
    EXEC(N'SELECT [PaymentSerie], COUNT(*) AS [Perfiles] FROM [InvoicingProfiles] GROUP BY [PaymentSerie] ORDER BY [PaymentSerie];');
