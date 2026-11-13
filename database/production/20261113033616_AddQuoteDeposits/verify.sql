-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 31M), la más reciente es
--    20261112165041_AddWhatsAppMessageMedia. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261113033616_AddQuoteDeposits.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes de rollback.sql: los negocios que piden anticipo, las versiones que lo piden (por estado:
--    1 enviada, 2 aceptada, 3 rechazada, 5 reemplazada) y los cobros de cotizaciones (por estado: 0
--    en espera, 1 aplicado, 2 vencido, 3 pagado y devuelto). Justo después de aplicar no sale ninguna
--    fila. rollback.sql se detiene con el error 50062, sin cambiar nada, mientras salga alguna: se
--    perdería. Un cobro aplicado (1) o pagado y devuelto (3) es dinero que se movió: el pago «En línea»
--    de su orden, o un pago y su devolución. Con uno así, no reviertas: vuelve al código anterior sin
--    rollback.sql. Con uno en espera (0), el cliente puede estar pagando: espera a que venza (30 minutos)
--    y consulta otra vez. Solo con cobros vencidos sin pago (2), para revertir de todos modos guarda
--    esta salida y solo entonces quita lo que la 31N agregó:
--        UPDATE [TenantSettings] SET [QuoteDepositPercent] = NULL WHERE [QuoteDepositPercent] IS NOT NULL;
--        UPDATE [Quotes] SET [DepositAmount] = NULL WHERE [DepositAmount] IS NOT NULL;
--        DELETE FROM [OnlineCheckouts] WHERE [QuoteId] IS NOT NULL AND [Status] = 2 AND [PaymentId] IS NULL;
--    Antes de aplicar y después de rollback.sql las columnas no existen y esta parte no corre.
IF COL_LENGTH(N'dbo.TenantSettings', N'QuoteDepositPercent') IS NOT NULL
    EXEC(N'SELECT t.[Slug] AS [Negocio], s.[QuoteDepositPercent] AS [Anticipo]
    FROM [TenantSettings] s
    JOIN [Tenants] t ON t.[Id] = s.[TenantId]
    WHERE s.[QuoteDepositPercent] IS NOT NULL
    ORDER BY t.[Slug];');

IF COL_LENGTH(N'dbo.Quotes', N'DepositAmount') IS NOT NULL
    EXEC(N'SELECT [Status] AS [Estado], COUNT(*) AS [Versiones], SUM([DepositAmount]) AS [Anticipos]
    FROM [Quotes]
    WHERE [DepositAmount] IS NOT NULL
    GROUP BY [Status]
    ORDER BY [Status];');

IF COL_LENGTH(N'dbo.OnlineCheckouts', N'QuoteId') IS NOT NULL
    EXEC(N'SELECT [Status] AS [Estado], COUNT(*) AS [Cobros], SUM([Amount]) AS [Monto]
    FROM [OnlineCheckouts]
    WHERE [QuoteId] IS NOT NULL
    GROUP BY [Status]
    ORDER BY [Status];');

-- 3. Después: las columnas nuevas (todas nulas): [TenantSettings].[QuoteDepositPercent] (int),
--    [Quotes].[DepositAmount] (decimal 18,2) y [OnlineCheckouts].[QuoteId] (uniqueidentifier); la
--    llave [FK_OnlineCheckouts_Quotes_QuoteId] (sin cascada) y el índice [IX_OnlineCheckouts_QuoteId]
--    (filtrado); y las restricciones [CK_TenantSettings_QuoteDepositPercent], [CK_Quotes_Deposit],
--    [CK_OnlineCheckouts_Quote] y [CK_OnlineCheckouts_Outcome], que después menciona [QuoteId]. Antes
--    de aplicar y después de rollback.sql las tres primeras partes no devuelven filas, y
--    [CK_OnlineCheckouts_Outcome] no menciona [QuoteId].
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[precision] AS [Precision], c.[scale] AS [Escala], c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'QuoteDepositPercent')
    OR (c.[object_id] = OBJECT_ID(N'dbo.Quotes') AND c.[name] = N'DepositAmount')
    OR (c.[object_id] = OBJECT_ID(N'dbo.OnlineCheckouts') AND c.[name] = N'QuoteId')
ORDER BY [Tabla];

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Tabla], fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[name] = N'FK_OnlineCheckouts_Quotes_QuoteId';

SELECT i.[name] AS [Indice], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[name] = N'IX_OnlineCheckouts_QuoteId';

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[name] IN (N'CK_TenantSettings_QuoteDepositPercent', N'CK_Quotes_Deposit', N'CK_OnlineCheckouts_Quote', N'CK_OnlineCheckouts_Outcome')
ORDER BY cc.[name];

-- 4. Antes y después: cuántas cotizaciones y cobros en línea hay. La migración no cambia filas: solo
--    cambian por lo que pase entre las dos corridas.
SELECT (SELECT COUNT(*) FROM [Quotes]) AS [Cotizaciones],
    (SELECT COUNT(*) FROM [OnlineCheckouts]) AS [Cobros];
