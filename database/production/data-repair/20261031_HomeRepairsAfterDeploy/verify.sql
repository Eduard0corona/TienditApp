-- =====================================================================================
-- TienditApp (FoodParkDigital): oficios que quedaron fuera de «Hogar y reparaciones» porque se
-- registraron entre apply.sql de 20261031085333_AddHomeVisitRequests y el despliegue de la Fase 29A.
-- Solo lee. Ver README.md.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.TenantSettings', N'HomeVisitRequestsEnabled') IS NULL
    THROW 50000, N'Apply 20261031085333_AddHomeVisitRequests first: there is nothing to repair before it.', 1;
GO

-- 1. Los que repair.sql mueve a 11: oficios cuya categoría sigue la que su giro daba antes de la
--    Fase 29A y que su dueño nunca eligió. La misma regla que la migración; después de repair.sql, cero.
SELECT t.[Id], t.[Name], t.[Industry], t.[DirectoryCategory] AS [Categoria]
FROM [Tenants] AS t
WHERE LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI IN (
        N'Carpinteria', N'Herreria y soldadura', N'Electricista', N'Plomeria', N'Aire acondicionado',
        N'Fumigacion', N'Refrigeracion y linea blanca', N'Cerrajeria', N'Jardineria', N'Limpieza del hogar',
        N'Pintura e impermeabilizacion')
    AND t.[DirectoryCategory] = CASE
        WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI IN (
            N'Carpinteria', N'Herreria y soldadura', N'Electricista', N'Plomeria', N'Aire acondicionado') THEN 9
        WHEN t.[BusinessType] = 0 THEN 0
        WHEN t.[BusinessType] = 2 THEN 9
        ELSE 10
    END
    AND NOT EXISTS (
        SELECT 1 FROM [AuditLogs] AS a
        WHERE a.[EntityName] = N'Tenant' AND a.[Action] = N'DirectoryProfileUpdated'
            AND a.[EntityId] = LOWER(CONVERT(nvarchar(36), t.[Id])))
ORDER BY t.[CreatedAt];

-- 2. Para revisar a mano (repair.sql no los toca): negocios en 11 sin giro de oficio y sin una
--    decisión del dueño. Un oficio que cambió de giro mientras corría la versión anterior. Su dueño
--    elige la categoría en Configuración › Cómo te ven en TienditApp.
SELECT t.[Id], t.[Name], t.[Industry], t.[DirectoryCategory] AS [Categoria]
FROM [Tenants] AS t
WHERE t.[DirectoryCategory] = 11
    AND LTRIM(RTRIM(ISNULL(t.[Industry], N''))) COLLATE Latin1_General_CI_AI NOT IN (
        N'Carpinteria', N'Herreria y soldadura', N'Electricista', N'Plomeria', N'Aire acondicionado',
        N'Fumigacion', N'Refrigeracion y linea blanca', N'Cerrajeria', N'Jardineria', N'Limpieza del hogar',
        N'Pintura e impermeabilizacion')
    AND NOT EXISTS (
        SELECT 1 FROM [AuditLogs] AS a
        WHERE a.[EntityName] = N'Tenant' AND a.[Action] = N'DirectoryProfileUpdated'
            AND a.[EntityId] = LOWER(CONVERT(nvarchar(36), t.[Id])))
ORDER BY t.[CreatedAt];
