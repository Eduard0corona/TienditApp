-- =====================================================================================
-- TienditApp (FoodParkDigital): mueve a «Hogar y reparaciones» (11) los oficios que se registraron
-- entre apply.sql de 20261031085333_AddHomeVisitRequests y el despliegue de la Fase 29A: la versión
-- anterior les dio la categoría de antes y la nueva ya no la ve como la de su giro. Córrelo con la
-- Fase 29A desplegada en todas las instancias, después de revisar verify.sql; ver README.md.
-- La misma regla que la migración: solo la categoría de [Tenants], de los oficios que la siguen y que
-- su dueño nunca eligió. Lista cada negocio movido: guarda la salida en el registro del cambio.
-- Idempotente: otra ejecución no encuentra a nadie.
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -W -s "|" -f o:65001 -i repair.sql -o repair-salida.txt
-- =====================================================================================
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH(N'dbo.TenantSettings', N'HomeVisitRequestsEnabled') IS NULL
    THROW 50000, N'Apply 20261031085333_AddHomeVisitRequests first: there is nothing to repair before it.', 1;
GO

BEGIN TRANSACTION;

UPDATE t SET [DirectoryCategory] = 11
OUTPUT inserted.[Id], inserted.[Name], inserted.[Industry], deleted.[DirectoryCategory] AS [Antes], inserted.[DirectoryCategory] AS [Ahora]
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
            AND a.[EntityId] = LOWER(CONVERT(nvarchar(36), t.[Id])));

COMMIT;
GO
