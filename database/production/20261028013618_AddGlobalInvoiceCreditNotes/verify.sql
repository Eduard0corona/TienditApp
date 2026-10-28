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
--    del SAT (los «Otro» y con saldo a favor no llevan nota). Al desplegar, TienditApp emite por su
--    cuenta la nota de crédito de cada uno, con la fecha de ese día: si alguno ya tiene su CFDI de
--    egreso hecho a mano, avísalo antes de desplegar. La migración no los cambia.
IF OBJECT_ID(N'dbo.GlobalInvoiceOrders') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [ReembolsosEnFacturaGlobal]
        FROM [OrderRefunds] r
        WHERE r.[Method] NOT IN (4, 7)
            AND EXISTS (SELECT 1 FROM [GlobalInvoiceOrders] s
                INNER JOIN [GlobalInvoices] g ON g.[Id] = s.[GlobalInvoiceId]
                WHERE s.[OrderId] = r.[OrderId] AND s.[Released] = 0 AND g.[Status] = 1);');
