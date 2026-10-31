-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28O), la más reciente es
--    20261031053720_AddAccountantCfdiEmails. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261031085333_AddHomeVisitRequests.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas [TenantSettings].[HomeVisitRequestsEnabled] (bit, no nula, valor por
--    omisión 0), [HomeVisitServiceId] (uniqueidentifier, nula), [HomeVisitChargePolicy] (int, no
--    nula, valor por omisión 0) y [HomeVisitAddressNoticeDismissedAt] (datetimeoffset, nula), la
--    restricción [CK_TenantSettings_HomeVisitChargePolicy] (de 0 a 2) y
--    [CK_Tenants_DirectoryCategory] de 0 a 11. Antes de aplicar y después de rollback.sql no hay
--    columnas y la categoría va de 0 a 10.
SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulo],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc
    ON dc.[parent_object_id] = c.[object_id] AND dc.[parent_column_id] = c.[column_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings')
    AND c.[name] IN (N'HomeVisitRequestsEnabled', N'HomeVisitServiceId', N'HomeVisitChargePolicy',
        N'HomeVisitAddressNoticeDismissedAt')
ORDER BY c.[name];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] IN (N'CK_TenantSettings_HomeVisitChargePolicy', N'CK_Tenants_DirectoryCategory')
ORDER BY cc.[name];

-- 3. Antes y después: los negocios por categoría en Explorar, y los oficios por categoría. Después de
--    aplicar, los oficios cuya categoría seguía su giro ya están en 11 («Hogar y reparaciones»); los
--    que siguen en otra son los que su dueño eligió.
SELECT [DirectoryCategory] AS [Categoria], COUNT(*) AS [Negocios]
FROM [Tenants]
GROUP BY [DirectoryCategory]
ORDER BY [DirectoryCategory];

SELECT [DirectoryCategory] AS [Categoria], COUNT(*) AS [Oficios]
FROM [Tenants]
WHERE LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI IN (
        N'Carpinteria', N'Herreria y soldadura', N'Electricista', N'Plomeria', N'Aire acondicionado',
        N'Fumigacion', N'Refrigeracion y linea blanca', N'Cerrajeria', N'Jardineria', N'Limpieza del hogar',
        N'Pintura e impermeabilizacion')
GROUP BY [DirectoryCategory]
ORDER BY [DirectoryCategory];

-- 4. Después: lo que rollback.sql perdería. Se detiene con 50049 si [CambiosDeVisitas] o
--    [EligieronHogar] no son cero. [Reciben] cuenta los negocios con el interruptor prendido (los
--    oficios lo traen al registrarse) y [AvisoDescartado] los que descartaron el aviso de su
--    dirección. Justo después de aplicar: todo en cero. Antes de aplicar y después de rollback.sql
--    las columnas no existen y la consulta no corre.
IF COL_LENGTH(N'dbo.TenantSettings', N'HomeVisitRequestsEnabled') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [AuditLogs] WHERE [Action] = N''HomeVisitRequestsUpdated'') AS [CambiosDeVisitas],
        (SELECT COUNT(*) FROM [Tenants] AS t
            WHERE t.[DirectoryCategory] = 11 AND EXISTS (
                SELECT 1 FROM [AuditLogs] AS a
                WHERE a.[EntityName] = N''Tenant'' AND a.[Action] = N''DirectoryProfileUpdated''
                    AND a.[EntityId] = LOWER(CONVERT(nvarchar(36), t.[Id])))) AS [EligieronHogar],
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [HomeVisitRequestsEnabled] = 1) AS [Reciben],
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [HomeVisitAddressNoticeDismissedAt] IS NOT NULL) AS [AvisoDescartado];');
