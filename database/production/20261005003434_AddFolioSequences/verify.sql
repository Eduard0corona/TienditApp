-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004232541_AddChargePermissionAndSmsCooldown; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes: por negocio, sus órdenes y su folio más alto, con el que arranca el contador. Si
--    [FolioMasAlto] es mayor que [Ordenes], la numeración ya tiene huecos (órdenes borradas): el
--    código actual, que numera contando, ya repetiría folios.
SELECT t.[Name] AS [Negocio],
    COUNT(o.[Id]) AS [Ordenes],
    ISNULL(MAX(CASE WHEN o.[OrderNumber] LIKE N'ORD-%'
        THEN TRY_CAST(SUBSTRING(o.[OrderNumber], 5, 18) AS bigint) END), 0) AS [FolioMasAlto]
FROM [Tenants] t
LEFT JOIN [Orders] o ON o.[TenantId] = t.[Id]
GROUP BY t.[Name]
ORDER BY t.[Name];

-- 3. Después: un contador 'ORD' por negocio, igual a su folio más alto de la consulta 2 (vacío
--    después de rollback.sql).
IF OBJECT_ID(N'dbo.FolioSequences') IS NOT NULL
    EXEC(N'SELECT t.[Name] AS [Negocio], s.[LastNumber] AS [UltimoFolio]
FROM [FolioSequences] s
INNER JOIN [Tenants] t ON t.[Id] = s.[TenantId]
WHERE s.[Scope] = N''ORD''
ORDER BY t.[Name];');

-- 4. Antes de volver al código anterior (que numera contando las órdenes del negocio y los
--    movimientos del día): negocios con huecos. Vacío: se puede volver sin choques. Si
--    [Ordenes] es menor que [UltimoFolio], su siguiente venta repetiría un folio; si
--    [MovimientosHoy] es menor que [UltimoMovimientoHoy], sus movimientos fallarían hasta que
--    cambie el día (UTC).
IF OBJECT_ID(N'dbo.FolioSequences') IS NOT NULL
    EXEC(N'DECLARE @today date = CAST(SYSUTCDATETIME() AS date);
DECLARE @scope nvarchar(40) = N''INV-'' + CONVERT(nchar(8), @today, 112);
WITH Numbering AS (
    SELECT t.[Name] AS [Negocio],
        (SELECT COUNT(*) FROM [Orders] o WHERE o.[TenantId] = t.[Id]) AS [Ordenes],
        (SELECT s.[LastNumber] FROM [FolioSequences] s WHERE s.[TenantId] = t.[Id] AND s.[Scope] = N''ORD'') AS [UltimoFolio],
        (SELECT COUNT(*) FROM [InventoryMovements] m WHERE m.[TenantId] = t.[Id]
            AND m.[MovementDate] >= @today AND m.[MovementDate] < DATEADD(DAY, 1, @today)) AS [MovimientosHoy],
        (SELECT s.[LastNumber] FROM [FolioSequences] s WHERE s.[TenantId] = t.[Id] AND s.[Scope] = @scope) AS [UltimoMovimientoHoy]
    FROM [Tenants] t)
SELECT *
FROM Numbering
WHERE [Ordenes] < ISNULL([UltimoFolio], 0) OR [MovimientosHoy] < ISNULL([UltimoMovimientoHoy], 0)
ORDER BY [Negocio];');

-- 5. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261005003434_AddFolioSequences';
