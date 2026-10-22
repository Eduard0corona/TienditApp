-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22I), la más reciente es
--    20261022011024_AddPlatformOrderIds. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261022033401_AddPlatformWithholdingCertificates.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla [PlatformWithholdingCertificates] con sus 4 restricciones y sus 2 índices
--    (el de [TenantId] y [Uuid], único y filtrado a [RemovedAt] IS NULL). Antes de aplicar y
--    después de rollback.sql: ninguna fila.
SELECT
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[parent_object_id] = OBJECT_ID(N'dbo.PlatformWithholdingCertificates')
ORDER BY cc.[name];

SELECT
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.PlatformWithholdingCertificates')
    AND i.[type] = 2
ORDER BY i.[name];

-- 3. Después: cuántos CFDI de retenciones subió cada negocio por plataforma, vigentes y quitados.
--    rollback.sql se detiene con 50033 si hay alguno. Justo después de aplicar: ninguno. Antes de
--    aplicar y después de rollback.sql la tabla no existe y la consulta no corre.
IF OBJECT_ID(N'dbo.PlatformWithholdingCertificates') IS NOT NULL
    EXEC(N'SELECT [Channel] AS [Plataforma],
            SUM(CASE WHEN [RemovedAt] IS NULL THEN 1 ELSE 0 END) AS [Vigentes],
            SUM(CASE WHEN [RemovedAt] IS NULL THEN 0 ELSE 1 END) AS [Quitados]
        FROM [PlatformWithholdingCertificates]
        GROUP BY [Channel]
        ORDER BY [Channel];');

-- 4. Antes y después de aplicar, igual: los pedidos de cada plataforma y lo que les calculó de
--    retenciones. La migración no cambia ninguno.
SELECT
    [Channel] AS [Plataforma],
    COUNT(*) AS [Pedidos],
    SUM([IsrWithheld]) AS [IsrRetenido],
    SUM([VatWithheld]) AS [IvaRetenido]
FROM [PlatformOrderDetails]
GROUP BY [Channel]
ORDER BY [Channel];
