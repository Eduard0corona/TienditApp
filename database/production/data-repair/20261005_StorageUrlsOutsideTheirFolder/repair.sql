-- =====================================================================================
-- TienditApp (FoodParkDigital): deja sin imagen los productos cuya dirección apunta a la carpeta
-- de otro negocio o sale de la suya con '..' (la API aceptaba cualquier dirección al crear un
-- producto hasta el 2026-10-01). Córrelo solo después de revisar verify.sql; ver README.md.
-- No borra archivos ni toca imágenes propias, externas, logos ni avatares. Lista cada producto
-- que cambia con su dirección anterior: guarda la salida en el registro del cambio.
-- Idempotente: una segunda ejecución no cambia nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -W -s "|" -f o:65001 -i repair.sql -o repair-salida.txt
-- =====================================================================================
SET NOCOUNT ON;
-- Las opciones que exigen los índices filtrados de Products (el sqlcmd clásico arranca con
-- QUOTED_IDENTIFIER OFF y el UPDATE fallaría).
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

-- La misma clasificación que verify.sql.
SELECT p.[Id]
INTO #Reparar
FROM [Products] p
CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(REPLACE(LOWER(p.[ImageUrl]),
    N'%2e', N'.'), N'%2f', N'/'), N'%5c', N'/'), N'\', N'/') AS [Ruta]) r
CROSS APPLY (SELECT CASE
    WHEN r.[Ruta] LIKE N'%/..' OR r.[Ruta] LIKE N'%/../%' OR r.[Ruta] LIKE N'%/..[?#]%' THEN N'RutaInvalida'
    WHEN r.[Ruta] LIKE N'%/products/' + LOWER(CONVERT(nvarchar(36), p.[TenantId])) + N'/%' THEN N'Propia'
    WHEN EXISTS (SELECT 1 FROM [Tenants] o WHERE o.[Id] <> p.[TenantId]
        AND r.[Ruta] LIKE N'%/' + LOWER(CONVERT(nvarchar(36), o.[Id])) + N'/%') THEN N'OtroNegocio'
    ELSE N'Externa' END AS [Clase]) c
WHERE p.[ImageUrl] <> N''
    AND c.[Clase] IN (N'RutaInvalida', N'OtroNegocio');

UPDATE p
SET p.[ImageUrl] = NULL,
    p.[UpdatedAt] = TODATETIMEOFFSET(SYSUTCDATETIME(), 0),
    p.[UpdatedBy] = N'data-repair 20261005_StorageUrlsOutsideTheirFolder'
OUTPUT inserted.[TenantId] AS [Negocio], inserted.[Id] AS [Producto], deleted.[ImageUrl] AS [DireccionAnterior]
FROM [Products] p
JOIN #Reparar x ON x.[Id] = p.[Id];

PRINT CONCAT(N'Productos que quedaron sin imagen: ', @@ROWCOUNT);

COMMIT;
