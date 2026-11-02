-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29G), la más reciente es
--    20261102151555_AddQuoteOnlineReplies. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261102205836_AddAppointmentProblemPhotos.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla [AppointmentProblemPhotos] con sus columnas, la restricción de su lugar
--    (de 0 a 4), su llave a [Appointments] (se borra con la cita) y sus dos índices únicos. Antes de
--    aplicar y después de rollback.sql no existe y las consultas no devuelven filas.
SELECT c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo], c.[max_length] AS [Largo], c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.AppointmentProblemPhotos')
ORDER BY c.[column_id];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[parent_object_id] = OBJECT_ID(N'dbo.AppointmentProblemPhotos');

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Tabla], fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] = OBJECT_ID(N'dbo.AppointmentProblemPhotos');

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.AppointmentProblemPhotos') AND i.[type] > 0
ORDER BY i.[name];

-- 3. Después: cuántas fotos mandaron los clientes y de cuántas visitas. Justo después de aplicar: cero.
--    rollback.sql se detiene con 50056 si no es cero. Antes de aplicar y después de rollback.sql la
--    tabla no existe y esta parte no corre.
IF OBJECT_ID(N'dbo.AppointmentProblemPhotos', N'U') IS NOT NULL
    EXEC(N'SELECT
        COUNT(*) AS [Fotos],
        COUNT(DISTINCT [AppointmentId]) AS [Visitas]
    FROM [AppointmentProblemPhotos];');
