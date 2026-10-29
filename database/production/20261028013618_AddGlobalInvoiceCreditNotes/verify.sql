-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas; nunca
-- muestran datos fiscales de los clientes.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28H), la más reciente es
--    20261026201123_AddIssuedCfdiDateIndexes. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261028013618_AddGlobalInvoiceCreditNotes.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después de rollback.sql: [OrderInvoiceId] no acepta nulos y no hay [GlobalInvoiceId],
--    ni [CK_CreditNotes_Invoice], ni [IX_CreditNotes_GlobalInvoiceId]. Después de apply.sql:
--    [OrderInvoiceId] acepta nulos, existe [GlobalInvoiceId] (nula) y existen la restricción y el
--    índice.
SELECT c.[name] AS [Columna], c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.CreditNotes') AND c.[name] IN (N'OrderInvoiceId', N'GlobalInvoiceId')
ORDER BY c.[name];
SELECT
    (SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_CreditNotes_Invoice') AS [Restriccion],
    (SELECT COUNT(*) FROM sys.indexes WHERE [name] = N'IX_CreditNotes_GlobalInvoiceId') AS [Indice],
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE [name] = N'FK_CreditNotes_GlobalInvoices_GlobalInvoiceId') AS [LlaveForanea];

-- 3. Antes y después: las notas de crédito, y las de una factura global (solo después de apply.sql;
--    justo después, ninguna). Mientras exista una, rollback.sql se detiene con el error 50044.
IF COL_LENGTH(N'dbo.CreditNotes', N'GlobalInvoiceId') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [NotasDeCredito], COUNT([GlobalInvoiceId]) AS [DeFacturaGlobal]
        FROM [CreditNotes];');
ELSE IF OBJECT_ID(N'dbo.CreditNotes') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [NotasDeCredito] FROM [CreditNotes];');

-- 4. Antes y después: los reembolsos de ventas que cubre una factura global timbrada, con una forma
--    del SAT (los «Otro» y con saldo a favor no llevan nota), sin nota de crédito de esa global. Al
--    desplegar, TienditApp emite por su cuenta la nota de cada uno, con la fecha de ese día, y la
--    cuenta baja conforme lo hace (los que no pueda emitir, Pedidos dice por qué): si alguno ya
--    tiene su CFDI de egreso hecho a mano, avísalo antes de desplegar. La migración no los cambia.
IF COL_LENGTH(N'dbo.CreditNotes', N'GlobalInvoiceId') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [ReembolsosEnFacturaGlobalSinNota]
        FROM [OrderRefunds] r
        WHERE r.[Method] NOT IN (4, 7)
            AND EXISTS (SELECT 1 FROM [GlobalInvoiceOrders] s
                INNER JOIN [GlobalInvoices] g ON g.[Id] = s.[GlobalInvoiceId]
                WHERE s.[OrderId] = r.[OrderId] AND s.[Released] = 0 AND g.[Status] = 1
                    AND NOT EXISTS (SELECT 1 FROM [CreditNotes] n
                        WHERE n.[OrderRefundId] = r.[Id] AND n.[GlobalInvoiceId] = g.[Id]));');
ELSE IF OBJECT_ID(N'dbo.GlobalInvoiceOrders') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [ReembolsosEnFacturaGlobalSinNota]
        FROM [OrderRefunds] r
        WHERE r.[Method] NOT IN (4, 7)
            AND EXISTS (SELECT 1 FROM [GlobalInvoiceOrders] s
                INNER JOIN [GlobalInvoices] g ON g.[Id] = s.[GlobalInvoiceId]
                WHERE s.[OrderId] = r.[OrderId] AND s.[Released] = 0 AND g.[Status] = 1);');

-- 5. Antes de desplegar: las ventas devueltas en parte, pagadas por completo, sin condonar ni vender a
--    crédito, sin reembolsos «Otro» o con saldo a favor, sin factura propia viva y fuera de toda
--    factura global viva. Desde la Fase 28I la global las toma por su total y cada reembolso lleva su
--    nota: las de un periodo que ya tiene su global timbrada entran en la siguiente que se emita, como
--    ventas de periodos anteriores (su vista previa las cuenta). Es un tope: también cuenta las de
--    periodos sin global todavía, que esperan la suya, y las que siguen pendientes o abiertas. Solo
--    cuenta ventas y negocios.
IF OBJECT_ID(N'dbo.GlobalInvoiceOrders') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [VentasDevueltasEnParteSinCfdi], COUNT(DISTINCT o.[TenantId]) AS [Negocios]
        FROM [Orders] o
        WHERE o.[Status] <> 99 AND o.[Total] > 0 AND o.[PaidAmount] >= o.[Total]
            AND o.[RefundedAmount] > 0 AND o.[RefundedAmount] < o.[Total]
            AND o.[WrittenOffAmount] = 0 AND o.[SoldOnCredit] = 0
            AND NOT EXISTS (SELECT 1 FROM [OrderRefunds] r WHERE r.[OrderId] = o.[Id] AND r.[Method] IN (4, 7))
            AND NOT EXISTS (SELECT 1 FROM [OrderInvoices] i WHERE i.[OrderId] = o.[Id] AND i.[Status] IN (0, 1, 3))
            AND NOT EXISTS (SELECT 1 FROM [GlobalInvoiceOrders] s WHERE s.[OrderId] = o.[Id] AND s.[Released] = 0);');
