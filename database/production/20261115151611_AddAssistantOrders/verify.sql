-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 32A), la más reciente es
--    20261114111542_AddAppointmentDeposits. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261115151611_AddAssistantOrders.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes de rollback.sql: los negocios que apagaron los pedidos de asistentes de IA y los pedidos
--    que prepararon los asistentes (por estado: 0 sin enviar, 1 enviado; y si su enlace sigue abierto).
--    Justo después de aplicar no sale ninguna fila. rollback.sql se detiene con el error 50064, sin
--    cambiar nada, mientras haya un negocio que los apagó, un pedido enviado (1) o un enlace abierto:
--    se perdería. Un pedido enviado es un pedido del negocio y el registro de que vino de un asistente;
--    con uno así, no reviertas: vuelve al código anterior sin rollback.sql. Con enlaces abiertos, un
--    cliente puede estar por enviarlo: espera a que venzan (un día) y consulta otra vez. Para revertir
--    de todos modos sin pedidos enviados, guarda esta salida y solo entonces quita lo que la 32B agregó:
--        UPDATE [TenantSettings] SET [AssistantOrders] = 1 WHERE [AssistantOrders] = 0;
--        DELETE FROM [AssistantOrders] WHERE [Status] = 0;
--    Antes de aplicar y después de rollback.sql la columna y la tabla no existen y esta parte no corre.
IF COL_LENGTH(N'dbo.TenantSettings', N'AssistantOrders') IS NOT NULL
    EXEC(N'SELECT t.[Slug] AS [Negocio]
    FROM [TenantSettings] s
    JOIN [Tenants] t ON t.[Id] = s.[TenantId]
    WHERE s.[AssistantOrders] = 0
    ORDER BY t.[Slug];');

IF OBJECT_ID(N'dbo.AssistantOrders') IS NOT NULL
    EXEC(N'SELECT [Status] AS [Estado],
        CASE WHEN [ExpiresAt] > SYSDATETIMEOFFSET() THEN 1 ELSE 0 END AS [EnlaceAbierto],
        COUNT(*) AS [Pedidos]
    FROM [AssistantOrders]
    GROUP BY [Status], CASE WHEN [ExpiresAt] > SYSDATETIMEOFFSET() THEN 1 ELSE 0 END
    ORDER BY [Status], [EnlaceAbierto];');

-- 3. Después: la columna nueva [TenantSettings].[AssistantOrders] (bit, no nula, con su valor por
--    omisión 1); la tabla [AssistantOrders] con sus columnas; sus llaves
--    [FK_AssistantOrders_Tenants_TenantId] y [FK_AssistantOrders_Orders_OrderId] (sin cascada); sus
--    índices [IX_AssistantOrders_Token] (único), [IX_AssistantOrders_TenantId_Status_ExpiresAt],
--    [IX_AssistantOrders_ExpiresAt] e [IX_AssistantOrders_OrderId] (único, filtrado); y sus
--    restricciones [CK_AssistantOrders_Token], [CK_AssistantOrders_ItemsTotal],
--    [CK_AssistantOrders_Status], [CK_AssistantOrders_Outcome] y [CK_AssistantOrders_PostalCode].
--    Antes de aplicar y después de rollback.sql no devuelven filas.
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo], c.[is_nullable] AS [Nulo], OBJECT_DEFINITION(c.[default_object_id]) AS [PorOmision]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'AssistantOrders')
    OR c.[object_id] = OBJECT_ID(N'dbo.AssistantOrders')
ORDER BY [Tabla], c.[column_id];

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Tabla], fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] = OBJECT_ID(N'dbo.AssistantOrders')
ORDER BY fk.[name];

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.AssistantOrders') AND i.[name] LIKE N'IX[_]%'
ORDER BY i.[name];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[parent_object_id] = OBJECT_ID(N'dbo.AssistantOrders')
ORDER BY cc.[name];

-- 4. Antes y después: cuántos negocios tienen ajustes y cuántos pedidos hay. La migración no cambia
--    filas: solo cambian por lo que pase entre las dos corridas.
SELECT (SELECT COUNT(*) FROM [TenantSettings]) AS [Negocios],
    (SELECT COUNT(*) FROM [Orders]) AS [Pedidos];
