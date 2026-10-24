-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28C), la más reciente es
--    20261024014559_AddPaymentComplements. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261024084435_AddCreditNotes.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla nueva [CreditNotes], sus restricciones y sus índices, y la columna
--    [InvoicingProfiles].[CreditNoteSerie]. Antes de aplicar y después de rollback.sql no hay nada.
SELECT [name] AS [Tabla]
FROM sys.tables
WHERE [name] = N'CreditNotes';

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] LIKE N'CK[_]CreditNotes[_]%'
ORDER BY [Restriccion];

SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.CreditNotes') AND i.[name] IS NOT NULL
ORDER BY [Indice];

SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [Nulo],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.InvoicingProfiles') AND c.[name] = N'CreditNoteSerie';

-- 3. Después: las notas de crédito por estado. rollback.sql se detiene con 50040 si hay alguna.
--    Justo después de aplicar: todo en cero. Antes de aplicar y después de rollback.sql la tabla no
--    existe y la consulta no corre.
IF OBJECT_ID(N'dbo.CreditNotes') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [CreditNotes] WHERE [Status] = 0) AS [Timbrando],
        (SELECT COUNT(*) FROM [CreditNotes] WHERE [Status] = 1) AS [Timbradas],
        (SELECT COUNT(*) FROM [CreditNotes] WHERE [Status] = 2) AS [Rechazadas],
        (SELECT COUNT(*) FROM [CreditNotes] WHERE [Status] = 3) AS [Cancelandose],
        (SELECT COUNT(*) FROM [CreditNotes] WHERE [Status] = 4) AS [Canceladas];');

-- 4. Antes y después: los reembolsos y las condonaciones vigentes de pedidos con una factura viva, los
--    que tendrán (o ya tienen) su nota de crédito. La migración no los cambia.
IF OBJECT_ID(N'dbo.OrderInvoices') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [OrderRefunds] r
            WHERE EXISTS (SELECT 1 FROM [OrderInvoices] i WHERE i.[OrderId] = r.[OrderId] AND i.[Status] IN (0, 1, 3))) AS [ReembolsosConFactura],
        (SELECT COUNT(*) FROM [OrderWriteOffs] w
            WHERE w.[ReversedAt] IS NULL
                AND EXISTS (SELECT 1 FROM [OrderInvoices] i WHERE i.[OrderId] = w.[OrderId] AND i.[Status] IN (0, 1, 3))) AS [CondonacionesConFactura];');

-- 5. Después: la serie de las notas de crédito de cada perfil de facturación (todas «NC» justo
--    después de aplicar). Antes de aplicar y después de rollback.sql la columna no existe y la
--    consulta no corre.
IF COL_LENGTH(N'dbo.InvoicingProfiles', N'CreditNoteSerie') IS NOT NULL
    EXEC(N'SELECT [CreditNoteSerie], COUNT(*) AS [Perfiles] FROM [InvoicingProfiles] GROUP BY [CreditNoteSerie] ORDER BY [CreditNoteSerie];');
