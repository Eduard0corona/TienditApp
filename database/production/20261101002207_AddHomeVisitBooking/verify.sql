-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29A), la más reciente es
--    20261031085333_AddHomeVisitRequests. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261101002207_AddHomeVisitBooking.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas [AppointmentVisits].[ChargePolicy] (int, nula) y
--    [Appointments].[CustomerEmail] (nvarchar, nula, 254 caracteres, que sys.columns cuenta como 508
--    bytes) y la restricción [CK_AppointmentVisits_ChargePolicy]. Antes de aplicar y después de
--    rollback.sql no existen.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Bytes],
    c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.AppointmentVisits') AND c.[name] = N'ChargePolicy')
    OR (c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] = N'CustomerEmail')
ORDER BY [Tabla], c.[name];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_AppointmentVisits_ChargePolicy';

-- 3. Antes y después: las citas en la dirección del cliente y las solicitudes en línea por confirmar.
--    Después, [PedidasEnLinea] y [ConCorreo] son lo que rollback.sql perdería: se detiene con 50050
--    si alguna no es cero. Justo después de aplicar: las dos en cero. Antes de aplicar y después de
--    rollback.sql las columnas no existen y esa parte no corre.
SELECT
    (SELECT COUNT(*) FROM [AppointmentVisits]) AS [Visitas],
    (SELECT COUNT(*) FROM [Appointments] WHERE [Channel] = 1 AND [Status] = 5) AS [SolicitudesPorConfirmar];

IF COL_LENGTH(N'dbo.AppointmentVisits', N'ChargePolicy') IS NOT NULL
    AND COL_LENGTH(N'dbo.Appointments', N'CustomerEmail') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [AppointmentVisits] WHERE [ChargePolicy] IS NOT NULL) AS [PedidasEnLinea],
        (SELECT COUNT(*) FROM [AppointmentVisits] WHERE [ChargePolicy] = 1) AS [SinCosto],
        (SELECT COUNT(*) FROM [Appointments] WHERE [CustomerEmail] IS NOT NULL) AS [ConCorreo];');
