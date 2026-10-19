-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24I), la más reciente es
--    20261019015524_AddOnlinePayments. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261019150436_AddOnlineBookingStaff.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las tablas nuevas con sus índices (el único por negocio y cuenta, y los de las
--    llaves) y sus llaves foráneas con lo que hacen al borrar. Antes de aplicar y después de
--    rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico]
FROM sys.indexes i
WHERE i.[object_id] IN (OBJECT_ID(N'dbo.OnlineBookingStaff'), OBJECT_ID(N'dbo.OnlineBookingStaffServices'))
    AND i.[name] IS NOT NULL
ORDER BY [Tabla], i.[name];

SELECT
    OBJECT_NAME(fk.[parent_object_id]) AS [Tabla],
    fk.[name] AS [Llave],
    OBJECT_NAME(fk.[referenced_object_id]) AS [Hacia],
    fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] IN (OBJECT_ID(N'dbo.OnlineBookingStaff'), OBJECT_ID(N'dbo.OnlineBookingStaffServices'))
ORDER BY [Tabla], fk.[name];

-- 3. Después: cuántas personas ofrecen los negocios en sus citas en línea, cuántas las agendan ahora,
--    en cuántos negocios, y cuántos servicios elegidos tienen (rollback.sql se detiene con 50025 si
--    hay una persona). Justo después de aplicar: ninguna. Antes de aplicar y después de rollback.sql
--    las tablas no existen y la consulta no corre.
IF OBJECT_ID(N'dbo.OnlineBookingStaff') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [Personas], SUM(CASE WHEN [Enabled] = 1 THEN 1 ELSE 0 END) AS [Encendidas],
        COUNT(DISTINCT [TenantId]) AS [Negocios],
        (SELECT COUNT(*) FROM [OnlineBookingStaffServices]) AS [ServiciosElegidos]
    FROM [OnlineBookingStaff];');

-- 4. Después: citas en línea con quien las atiende (las de esta fase en adelante) y sin nadie
--    (las anteriores, o de un negocio que no eligió a nadie). Solo lee [Appointments], que no cambia.
SELECT
    SUM(CASE WHEN [StaffUserId] IS NOT NULL THEN 1 ELSE 0 END) AS [EnLineaConPersona],
    SUM(CASE WHEN [StaffUserId] IS NULL THEN 1 ELSE 0 END) AS [EnLineaSinPersona]
FROM [Appointments]
WHERE [Channel] = 1;
