-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 31N), la más reciente es
--    20261113033616_AddQuoteDeposits. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261114111542_AddAppointmentDeposits.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes de rollback.sql: los negocios que piden anticipo para reservar, las citas con anticipo
--    (por dónde está: 1 retenido, 2 en su venta, 3 devuelto, 4 con el negocio) y los cobros de citas
--    (por estado: 0 en espera, 1 reservó la cita, 2 vencido, 3 pagado y devuelto). Justo después de
--    aplicar no sale ninguna fila. rollback.sql se detiene con el error 50063, sin cambiar nada,
--    mientras salga alguna: se perdería. Una cita con anticipo, o un cobro que reservó (1) o pagado y
--    devuelto (3), es dinero que se movió: lo retenido espera su venta o su devolución, el de una
--    venta es su pago «En línea», y lo devuelto o con el negocio es historial. Con uno así, no
--    reviertas: vuelve al código anterior sin rollback.sql. Con uno en espera (0), el cliente puede
--    estar pagando: espera a que venza (30 minutos) y consulta otra vez. Solo con cobros vencidos sin
--    pago (2), para revertir de todos modos guarda esta salida y solo entonces quita lo que la 31O
--    agregó:
--        UPDATE [TenantSettings] SET [BookingDepositPercent] = NULL WHERE [BookingDepositPercent] IS NOT NULL;
--        DELETE FROM [OnlineCheckouts] WHERE [ForAppointment] = 1 AND [Status] = 2 AND [PaymentId] IS NULL;
--    Antes de aplicar y después de rollback.sql las columnas no existen y esta parte no corre.
IF COL_LENGTH(N'dbo.TenantSettings', N'BookingDepositPercent') IS NOT NULL
    EXEC(N'SELECT t.[Slug] AS [Negocio], s.[BookingDepositPercent] AS [Anticipo]
    FROM [TenantSettings] s
    JOIN [Tenants] t ON t.[Id] = s.[TenantId]
    WHERE s.[BookingDepositPercent] IS NOT NULL
    ORDER BY t.[Slug];');

IF COL_LENGTH(N'dbo.Appointments', N'DepositStatus') IS NOT NULL
    EXEC(N'SELECT [DepositStatus] AS [Anticipo], COUNT(*) AS [Citas], SUM([DepositAmount]) AS [Monto]
    FROM [Appointments]
    WHERE [DepositAmount] IS NOT NULL
    GROUP BY [DepositStatus]
    ORDER BY [DepositStatus];');

IF COL_LENGTH(N'dbo.OnlineCheckouts', N'ForAppointment') IS NOT NULL
    EXEC(N'SELECT [Status] AS [Estado], COUNT(*) AS [Cobros], SUM([Amount]) AS [Monto]
    FROM [OnlineCheckouts]
    WHERE [ForAppointment] = 1
    GROUP BY [Status]
    ORDER BY [Status];');

-- 3. Después: las columnas nuevas: [TenantSettings].[BookingDepositPercent] (int),
--    [Appointments].[DepositAmount] (decimal 18,2), [Appointments].[DepositStatus] (int) y
--    [OnlineCheckouts].[AppointmentId] (uniqueidentifier), nulas, y [OnlineCheckouts].[ForAppointment]
--    (bit, no nula, con su valor por omisión 0); la llave [FK_OnlineCheckouts_Appointments_AppointmentId]
--    (sin cascada); los índices [IX_OnlineCheckouts_AppointmentId] (único, filtrado) e
--    [IX_Appointments_DepositHeld_Requests] (filtrado); y las restricciones
--    [CK_TenantSettings_BookingDepositPercent], [CK_Appointments_Deposit],
--    [CK_OnlineCheckouts_Appointment], [CK_OnlineCheckouts_Outcome] y [CK_OnlinePaymentRefunds_Link],
--    las dos últimas ya con [AppointmentId] y la razón 7. Antes de aplicar y después de rollback.sql
--    las cuatro primeras partes no devuelven filas, [CK_OnlineCheckouts_Outcome] no menciona
--    [AppointmentId] y [CK_OnlinePaymentRefunds_Link] no menciona la razón 7.
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[precision] AS [Precision], c.[scale] AS [Escala], c.[is_nullable] AS [Nulo],
    OBJECT_DEFINITION(c.[default_object_id]) AS [PorOmision]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'BookingDepositPercent')
    OR (c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] IN (N'DepositAmount', N'DepositStatus'))
    OR (c.[object_id] = OBJECT_ID(N'dbo.OnlineCheckouts') AND c.[name] IN (N'ForAppointment', N'AppointmentId'))
ORDER BY [Tabla], [Columna];

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Tabla], fk.[delete_referential_action_desc] AS [AlBorrar]
FROM sys.foreign_keys fk
WHERE fk.[name] = N'FK_OnlineCheckouts_Appointments_AppointmentId';

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[name] IN (N'IX_OnlineCheckouts_AppointmentId', N'IX_Appointments_DepositHeld_Requests')
ORDER BY i.[name];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[name] IN (N'CK_TenantSettings_BookingDepositPercent', N'CK_Appointments_Deposit', N'CK_OnlineCheckouts_Appointment',
    N'CK_OnlineCheckouts_Outcome', N'CK_OnlinePaymentRefunds_Link')
ORDER BY cc.[name];

-- 4. Antes y después: cuántas citas, cobros en línea y devoluciones hay. La migración no cambia
--    filas: solo cambian por lo que pase entre las dos corridas.
SELECT (SELECT COUNT(*) FROM [Appointments]) AS [Citas],
    (SELECT COUNT(*) FROM [OnlineCheckouts]) AS [Cobros],
    (SELECT COUNT(*) FROM [OnlinePaymentRefunds]) AS [Devoluciones];
