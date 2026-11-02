-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29E), la más reciente es
--    20261102014702_AddQuoteConversion. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261102073115_AddAppointmentWorkOrder.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [Appointments].[WorkOrderId] (uniqueidentifier, nula), el índice
--    IX_Appointments_WorkOrderId, la llave a [Orders] y CK_Appointments_Work. Antes de aplicar y
--    después de rollback.sql no existen.
SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] = N'WorkOrderId';

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.Appointments') AND i.[name] = N'IX_Appointments_WorkOrderId';

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Referencia]
FROM sys.foreign_keys fk
WHERE fk.[name] = N'FK_Appointments_Orders_WorkOrderId';

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_Appointments_Work';

-- 3. Después: lo que rollback.sql perdería; se detiene con 50054 si no es cero. Justo después de
--    aplicar: cero. Antes de aplicar y después de rollback.sql la columna no existe y esa parte no
--    corre.
IF COL_LENGTH(N'dbo.Appointments', N'WorkOrderId') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [Appointments] WHERE [WorkOrderId] IS NOT NULL) AS [CitasDelTrabajo],
        (SELECT COUNT(DISTINCT [WorkOrderId]) FROM [Appointments] WHERE [WorkOrderId] IS NOT NULL) AS [OrdenesConCitas];');
