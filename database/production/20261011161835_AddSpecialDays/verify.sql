-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 20), la más reciente es
--    20261010074254_AddOpeningHours. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas de [SpecialDays]: [Date] date, las cuatro horas time(0) ([Escala] 0),
--    [Reason] nvarchar(40) ([LongitudMaxima] 80, en bytes) y [Kind] y [Revision] int. Antes de
--    aplicar y después de rollback.sql: ninguna fila.
SELECT
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [LongitudMaxima],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.SpecialDays')
ORDER BY c.[column_id];

-- 3. Después: las cinco restricciones CHECK (CK_SpecialDays_Kind, _Openings, _Reason, _Revision y
--    _SecondOpening) y los índices: la llave, IX_SpecialDays_TenantId_Date ([Unico] 1) e
--    IX_SpecialDays_TenantId_PeriodId. La de _Reason dice ([Reason] IS NULL OR len([Reason])>(0)):
--    acepta un motivo hecho solo de emojis y rechaza uno vacío o de puros espacios.
--    Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT N'CHECK' AS [Tipo], [name] AS [Nombre], NULL AS [Unico], [definition] AS [Definicion]
FROM sys.check_constraints
WHERE [parent_object_id] = OBJECT_ID(N'dbo.SpecialDays')
UNION ALL
SELECT N'INDEX', [name], CAST([is_unique] AS int), NULL
FROM sys.indexes
WHERE [object_id] = OBJECT_ID(N'dbo.SpecialDays') AND [type] > 0
ORDER BY [Tipo], [Nombre];

-- 4. Después: días especiales guardados. Justo después de aplicar, [Filas] es 0: ningún negocio
--    cambia su horario hasta que su dueño marque un día. Antes de rollback.sql: lo que se perdería
--    ([DesdeHoy] son las fechas de hoy en adelante, en la hora del centro de México).
IF OBJECT_ID(N'[SpecialDays]') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [Filas], COUNT(DISTINCT [TenantId]) AS [Negocios],
    COUNT(CASE WHEN [Date] >= CAST(SYSDATETIMEOFFSET() AT TIME ZONE ''Central Standard Time (Mexico)'' AS date) THEN 1 END) AS [DesdeHoy]
FROM [SpecialDays];');

-- 5. Después: quedó registrada la migración. Después de rollback.sql: ninguna fila.
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261011161835_AddSpecialDays';
