-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 31H), la más reciente es
--    20261110201340_AddAppointmentStaffForeignKey. Si es anterior, revisa primero el verify.sql de
--    su carpeta: apply.sql también aplica las que falten. Después: 20261111060433_AddOnlineBookingStaffAbsencesAndPhotos.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [OnlineBookingStaff].[PhotoUrl] (nvarchar, 1000 bytes, nula) y la tabla
--    [OnlineBookingStaffAbsences] con sus columnas, la restricción de sus días ([LastDay] >= [FirstDay]),
--    sus llaves (a [OnlineBookingStaff], se borra con la persona; a [Tenants], sin cascada) y sus
--    índices. Antes de aplicar y después de rollback.sql no existen y las consultas no devuelven filas.
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo], c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.OnlineBookingStaff') AND c.[name] = N'PhotoUrl')
    OR c.[object_id] = OBJECT_ID(N'dbo.OnlineBookingStaffAbsences')
ORDER BY [Tabla], c.[column_id];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[parent_object_id] = OBJECT_ID(N'dbo.OnlineBookingStaffAbsences');

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Tabla], fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] = OBJECT_ID(N'dbo.OnlineBookingStaffAbsences')
ORDER BY fk.[name];

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.OnlineBookingStaffAbsences') AND i.[type] > 0
ORDER BY i.[name];

-- 3. Antes de rollback.sql (lo bloquea): por negocio, cuántas ausencias y cuántas fotos de quien
--    atiende guardó. Justo después de aplicar no sale ninguna fila. rollback.sql se detiene con el
--    error 50059, sin cambiar nada, mientras salga alguna: se perderían. Para revertir de todos
--    modos, guarda esta salida, avisa a esos negocios y solo entonces bórralas:
--        DELETE FROM [OnlineBookingStaffAbsences];
--        UPDATE [OnlineBookingStaff] SET [PhotoUrl] = NULL WHERE [PhotoUrl] IS NOT NULL;
--    Los archivos de las fotos se quedan en el almacenamiento (staff/<negocio>). Antes de aplicar y
--    después de rollback.sql la tabla no existe y esta parte no corre.
IF OBJECT_ID(N'dbo.OnlineBookingStaffAbsences', N'U') IS NOT NULL
    EXEC(N'SELECT t.[Slug] AS [Negocio],
        (SELECT COUNT(*) FROM [OnlineBookingStaffAbsences] a WHERE a.[TenantId] = t.[Id]) AS [Ausencias],
        (SELECT COUNT(*) FROM [OnlineBookingStaff] s WHERE s.[TenantId] = t.[Id] AND s.[PhotoUrl] IS NOT NULL) AS [Fotos]
    FROM [Tenants] t
    WHERE EXISTS (SELECT 1 FROM [OnlineBookingStaffAbsences] a WHERE a.[TenantId] = t.[Id])
        OR EXISTS (SELECT 1 FROM [OnlineBookingStaff] s WHERE s.[TenantId] = t.[Id] AND s.[PhotoUrl] IS NOT NULL)
    ORDER BY t.[Slug];');

-- 4. Antes y después: cuántas personas atienden citas en línea (y cuántas encendidas). La migración
--    no cambia filas: solo cambia por lo que los negocios guarden entre las dos corridas.
SELECT COUNT(*) AS [Personas], COALESCE(SUM(CASE WHEN [Enabled] = 1 THEN 1 ELSE 0 END), 0) AS [Encendidas]
FROM [OnlineBookingStaff];
