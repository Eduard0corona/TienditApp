-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas; nunca
-- muestran datos fiscales de los clientes.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28I), la más reciente es
--    20261028013618_AddGlobalInvoiceCreditNotes. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261029163102_AddGlobalInvoiceWriteOffNotes.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después de rollback.sql: [CK_CreditNotes_Invoice] exige un reembolso en la nota de una
--    factura global ([ExigeReembolso] = 1). Después de apply.sql: ya no lo exige (0), y sigue
--    exigiendo una exhibición ([AfterInstallment] = 0).
SELECT [name] AS [Restriccion],
    CASE WHEN [definition] LIKE N'%OrderRefundId%' THEN 1 ELSE 0 END AS [ExigeReembolso]
FROM sys.check_constraints
WHERE [name] = N'CK_CreditNotes_Invoice';

-- 3. Antes y después: las notas de crédito de una factura global, y las de una condonación (solo
--    después de apply.sql; justo después, ninguna). Mientras exista una de condonación,
--    rollback.sql se detiene con el error 50045.
SELECT COUNT(*) AS [NotasDeFacturaGlobal], COUNT([OrderWriteOffId]) AS [DeCondonacion]
FROM [CreditNotes]
WHERE [GlobalInvoiceId] IS NOT NULL;

-- 4. Antes de desplegar: las ventas que no se vendieron a crédito, con una condonación en pie y ya
--    saldadas con ella (lo cobrado más lo condonado cubre el total), sin reembolsos «Otro» o con saldo
--    a favor, sin factura propia viva y fuera de toda factura global viva. Desde la Fase 28J la global
--    las toma por su total y cada condonación lleva su nota: las de un periodo que ya tiene su global
--    timbrada entran en la siguiente que se emita, como ventas de periodos anteriores (su vista previa
--    las cuenta). Es un tope: también cuenta las de periodos sin global todavía, que esperan la suya,
--    las que siguen pendientes o abiertas y las condonadas completas sin cobros. Solo cuenta ventas y
--    negocios.
SELECT COUNT(*) AS [VentasCondonadasSinCfdi], COUNT(DISTINCT o.[TenantId]) AS [Negocios]
FROM [Orders] o
WHERE o.[Status] <> 99 AND o.[Total] > 0 AND o.[SoldOnCredit] = 0
    AND o.[WrittenOffAmount] > 0 AND o.[PaidAmount] + o.[WrittenOffAmount] >= o.[Total]
    AND NOT EXISTS (SELECT 1 FROM [OrderRefunds] r WHERE r.[OrderId] = o.[Id] AND r.[Method] IN (4, 7))
    AND NOT EXISTS (SELECT 1 FROM [OrderInvoices] i WHERE i.[OrderId] = o.[Id] AND i.[Status] IN (0, 1, 3))
    AND NOT EXISTS (SELECT 1 FROM [GlobalInvoiceOrders] s WHERE s.[OrderId] = o.[Id] AND s.[Released] = 0);

-- 5. Después de desplegar: las condonaciones en pie por motivo (4 es «Descuento o bonificación», que
--    llega con la Fase 28J; antes, ninguna). La migración no las cambia.
SELECT [Reason] AS [Motivo], COUNT(*) AS [Condonaciones]
FROM [OrderWriteOffs]
WHERE [ReversedAt] IS NULL
GROUP BY [Reason]
ORDER BY [Reason];
