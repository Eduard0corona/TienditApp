-- =====================================================================================
-- TienditApp (FoodParkDigital): fotos de producto, logos y avatares guardados con una dirección
-- fuera de la carpeta de su dueño. Solo lee. Ver README.md.
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

-- Cada dirección se clasifica como la interpreta un navegador: en minúsculas, con '\' como '/' y
-- con '.', '/' y '\' ya decodificados (%2e, %2f, %5c).
--   Propia        dentro de la carpeta de su dueño: products/{negocio}, logos/{negocio} y, para
--                 avatares, avatars/users/{cuenta}, avatars/{un negocio de la cuenta} o
--                 avatars/00000000-0000-0000-0000-000000000000 (sesión sin negocio).
--   RutaInvalida  con un segmento '..': sale de la carpeta donde parece estar.
--   OtroNegocio   dentro de la carpeta de otro negocio.
--   Externa       cualquier otra (otro sitio u otra forma de dirección).
-- repair.sql solo cambia productos RutaInvalida u OtroNegocio. Este script no muestra direcciones.

-- 1. Conteo por tipo, clase y negocio de la fila (los avatares son de la cuenta, no de un negocio).
WITH Images AS (
    SELECT N'Producto' AS [Tipo], p.[TenantId] AS [Negocio], c.[Clase]
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
    UNION ALL
    SELECT N'Logo', t.[Id], c.[Clase]
    FROM [Tenants] t
    CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(REPLACE(LOWER(t.[LogoUrl]),
        N'%2e', N'.'), N'%2f', N'/'), N'%5c', N'/'), N'\', N'/') AS [Ruta]) r
    CROSS APPLY (SELECT CASE
        WHEN r.[Ruta] LIKE N'%/..' OR r.[Ruta] LIKE N'%/../%' OR r.[Ruta] LIKE N'%/..[?#]%' THEN N'RutaInvalida'
        WHEN r.[Ruta] LIKE N'%/logos/' + LOWER(CONVERT(nvarchar(36), t.[Id])) + N'/%' THEN N'Propia'
        WHEN EXISTS (SELECT 1 FROM [Tenants] o WHERE o.[Id] <> t.[Id]
            AND r.[Ruta] LIKE N'%/' + LOWER(CONVERT(nvarchar(36), o.[Id])) + N'/%') THEN N'OtroNegocio'
        ELSE N'Externa' END AS [Clase]) c
    WHERE t.[LogoUrl] <> N''
    UNION ALL
    SELECT N'Avatar', NULL, c.[Clase]
    FROM [AspNetUsers] u
    CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(REPLACE(LOWER(u.[AvatarUrl]),
        N'%2e', N'.'), N'%2f', N'/'), N'%5c', N'/'), N'\', N'/') AS [Ruta]) r
    CROSS APPLY (SELECT CASE
        WHEN r.[Ruta] LIKE N'%/..' OR r.[Ruta] LIKE N'%/../%' OR r.[Ruta] LIKE N'%/..[?#]%' THEN N'RutaInvalida'
        WHEN r.[Ruta] LIKE N'%/avatars/users/' + LOWER(CONVERT(nvarchar(36), u.[Id])) + N'/%'
            OR r.[Ruta] LIKE N'%/avatars/00000000-0000-0000-0000-000000000000/%'
            OR EXISTS (SELECT 1 FROM [TenantUsers] m WHERE m.[UserId] = u.[Id]
                AND r.[Ruta] LIKE N'%/avatars/' + LOWER(CONVERT(nvarchar(36), m.[TenantId])) + N'/%') THEN N'Propia'
        WHEN EXISTS (SELECT 1 FROM [Tenants] o
            WHERE r.[Ruta] LIKE N'%/' + LOWER(CONVERT(nvarchar(36), o.[Id])) + N'/%') THEN N'OtroNegocio'
        ELSE N'Externa' END AS [Clase]) c
    WHERE u.[AvatarUrl] <> N''
)
SELECT i.[Tipo], i.[Clase], i.[Negocio], t.[Name] AS [NombreDelNegocio], COUNT(*) AS [Filas]
FROM Images i
LEFT JOIN [Tenants] t ON t.[Id] = i.[Negocio]
GROUP BY i.[Tipo], i.[Clase], i.[Negocio], t.[Name]
ORDER BY i.[Tipo], i.[Clase], t.[Name];

-- 2. Los productos que repair.sql dejaría sin imagen, con el negocio cuya carpeta señalan.
SELECT p.[TenantId] AS [Negocio], t.[Name] AS [NombreDelNegocio], p.[Id] AS [Producto],
    p.[Name] AS [NombreDelProducto], c.[Clase],
    (SELECT TOP (1) o.[Name] FROM [Tenants] o WHERE o.[Id] <> p.[TenantId]
        AND r.[Ruta] LIKE N'%/' + LOWER(CONVERT(nvarchar(36), o.[Id])) + N'/%' ORDER BY o.[Name]) AS [CarpetaDe]
FROM [Products] p
JOIN [Tenants] t ON t.[Id] = p.[TenantId]
CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(REPLACE(LOWER(p.[ImageUrl]),
    N'%2e', N'.'), N'%2f', N'/'), N'%5c', N'/'), N'\', N'/') AS [Ruta]) r
CROSS APPLY (SELECT CASE
    WHEN r.[Ruta] LIKE N'%/..' OR r.[Ruta] LIKE N'%/../%' OR r.[Ruta] LIKE N'%/..[?#]%' THEN N'RutaInvalida'
    WHEN r.[Ruta] LIKE N'%/products/' + LOWER(CONVERT(nvarchar(36), p.[TenantId])) + N'/%' THEN N'Propia'
    WHEN EXISTS (SELECT 1 FROM [Tenants] o WHERE o.[Id] <> p.[TenantId]
        AND r.[Ruta] LIKE N'%/' + LOWER(CONVERT(nvarchar(36), o.[Id])) + N'/%') THEN N'OtroNegocio'
    ELSE N'Externa' END AS [Clase]) c
WHERE p.[ImageUrl] <> N''
    AND c.[Clase] IN (N'RutaInvalida', N'OtroNegocio')
ORDER BY t.[Name], p.[Name];
