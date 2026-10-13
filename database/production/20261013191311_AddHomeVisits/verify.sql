-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22D), la más reciente es
--    20261013085926_AddCourierSettlements. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261013191311_AddHomeVisits.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas ([TenantSettings].[HomeVisitTravelMinutes] int sin nulos;
--    [Appointments].[SaleOrderId] uniqueidentifier con nulos y [Version] timestamp, que es
--    rowversion, sin nulos) y las de las dos tablas nuevas, con nulos solo en [References]. [Largo]
--    va en bytes (nvarchar(60) es 120). Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [Largo],
    c.[precision] AS [Precision],
    c.[scale] AS [Escala],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'HomeVisitTravelMinutes')
    OR (c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] IN (N'SaleOrderId', N'Version'))
    OR c.[object_id] IN (OBJECT_ID(N'dbo.AppointmentVisits'), OBJECT_ID(N'dbo.OrderVisitCharges'))
ORDER BY [Tabla], c.[column_id];

-- 3. Después: las restricciones nuevas: CHECK (CK_TenantSettings_HomeVisitTravelMinutes;
--    CK_AppointmentVisits_PostalCode, _AddressLine y _Fee; CK_OrderVisitCharges_Fee y _FeeTaxRate),
--    el DEFAULT de [HomeVisitTravelMinutes] ((30)), las llaves (de [AppointmentVisits] a su cita y
--    de [OrderVisitCharges] a su pedido en cascada, CASCADE; de [OrderVisitCharges] a la cita y
--    FK_Appointments_Orders_SaleOrderId sin cascada, NO_ACTION) y los índices, entre ellos
--    IX_AppointmentVisits_AppointmentId e IX_OrderVisitCharges_OrderId ([Unico] 1). SQL Server
--    muestra las CHECK a su manera (por ejemplo, ([Fee]>(0))). Antes de aplicar y después de
--    rollback.sql: ninguna fila.
SELECT OBJECT_NAME([parent_object_id]) AS [Tabla], N'CHECK' AS [Tipo], [name] AS [Nombre], NULL AS [Unico],
    [definition] COLLATE DATABASE_DEFAULT AS [Definicion]
FROM sys.check_constraints
WHERE [name] = N'CK_TenantSettings_HomeVisitTravelMinutes'
    OR [parent_object_id] IN (OBJECT_ID(N'dbo.AppointmentVisits'), OBJECT_ID(N'dbo.OrderVisitCharges'))
UNION ALL
SELECT OBJECT_NAME(d.[parent_object_id]), N'DEFAULT', d.[name], NULL, d.[definition] COLLATE DATABASE_DEFAULT
FROM sys.default_constraints d
INNER JOIN sys.columns c ON c.[object_id] = d.[parent_object_id] AND c.[column_id] = d.[parent_column_id]
WHERE d.[parent_object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'HomeVisitTravelMinutes'
UNION ALL
SELECT OBJECT_NAME([parent_object_id]), N'FK', [name], NULL, [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [name] = N'FK_Appointments_Orders_SaleOrderId'
    OR [parent_object_id] IN (OBJECT_ID(N'dbo.AppointmentVisits'), OBJECT_ID(N'dbo.OrderVisitCharges'))
UNION ALL
SELECT OBJECT_NAME([object_id]), N'INDEX', [name], CAST([is_unique] AS int), [filter_definition] COLLATE DATABASE_DEFAULT
FROM sys.indexes
WHERE ([object_id] = OBJECT_ID(N'dbo.Appointments') AND [name] = N'IX_Appointments_SaleOrderId')
    OR ([object_id] IN (OBJECT_ID(N'dbo.AppointmentVisits'), OBJECT_ID(N'dbo.OrderVisitCharges')) AND [type] > 0)
ORDER BY [Tabla], [Tipo], [Nombre];

-- 4. Después de desplegar, y antes de volver a la versión anterior o de rollback.sql, por negocio:
--    las citas a domicilio (todas y las que siguen abiertas: agendadas, confirmadas o en servicio),
--    las ventas con cargo por visita y cuánto suman esos cargos, y las citas cobradas desde la venta.
--    Con alguna, rollback.sql se detiene (error 50015) sin cambiar nada. Volver la app atrás con
--    citas a domicilio abiertas: esa versión no muestra la dirección (avísale al equipo antes);
--    el cargo de una venta sigue en su total. Recién aplicado: ninguna fila.
IF OBJECT_ID(N'dbo.AppointmentVisits') IS NOT NULL
    EXEC(N'SELECT x.[TenantId] AS [Negocio], SUM(x.[Visitas]) AS [CitasADomicilio], SUM(x.[Abiertas]) AS [Abiertas],
    SUM(x.[Cargos]) AS [VentasConCargo], SUM(x.[Cargo]) AS [SumanLosCargos], SUM(x.[Cobradas]) AS [CitasCobradas]
FROM (
    SELECT v.[TenantId], 1 AS [Visitas], CASE WHEN a.[Status] IN (0, 1, 2) THEN 1 ELSE 0 END AS [Abiertas],
        0 AS [Cargos], 0 AS [Cargo], 0 AS [Cobradas]
    FROM [AppointmentVisits] v
    INNER JOIN [Appointments] a ON a.[Id] = v.[AppointmentId]
    UNION ALL
    SELECT [TenantId], 0, 0, 1, [Fee], 0
    FROM [OrderVisitCharges]
    UNION ALL
    SELECT [TenantId], 0, 0, 0, 0, 1
    FROM [Appointments]
    WHERE [SaleOrderId] IS NOT NULL
) x
GROUP BY x.[TenantId]
ORDER BY [CitasADomicilio] DESC;');

-- 5. Después: consistencia. Cada consulta debe dar 0 filas:
--    a. visitas, cargos por visita o citas cobradas de un negocio distinto al de su cita o su venta;
--    b. cargos por visita en una venta no cancelada que no es la venta de su cita;
--    c. citas cobradas que no quedaron completadas ([Estado] 3).
IF OBJECT_ID(N'dbo.AppointmentVisits') IS NOT NULL
BEGIN
    EXEC(N'SELECT N''Visita'' AS [Fila], v.[Id], v.[TenantId] AS [Negocio], a.[TenantId] AS [NegocioDeLaCita], NULL AS [NegocioDeLaVenta]
FROM [AppointmentVisits] v
INNER JOIN [Appointments] a ON a.[Id] = v.[AppointmentId]
WHERE v.[TenantId] <> a.[TenantId]
UNION ALL
SELECT N''Cargo'', c.[Id], c.[TenantId], a.[TenantId], o.[TenantId]
FROM [OrderVisitCharges] c
INNER JOIN [Appointments] a ON a.[Id] = c.[AppointmentId]
INNER JOIN [Orders] o ON o.[Id] = c.[OrderId]
WHERE c.[TenantId] <> a.[TenantId] OR c.[TenantId] <> o.[TenantId]
UNION ALL
SELECT N''Cita'', a.[Id], a.[TenantId], NULL, o.[TenantId]
FROM [Appointments] a
INNER JOIN [Orders] o ON o.[Id] = a.[SaleOrderId]
WHERE a.[TenantId] <> o.[TenantId];');
    EXEC(N'SELECT c.[OrderId], c.[AppointmentId], a.[SaleOrderId] AS [VentaDeLaCita]
FROM [OrderVisitCharges] c
INNER JOIN [Orders] o ON o.[Id] = c.[OrderId]
INNER JOIN [Appointments] a ON a.[Id] = c.[AppointmentId]
WHERE o.[Status] <> 99 AND (a.[SaleOrderId] IS NULL OR a.[SaleOrderId] <> c.[OrderId]);');
    EXEC(N'SELECT [Id], [Status] AS [Estado], [SaleOrderId]
FROM [Appointments]
WHERE [SaleOrderId] IS NOT NULL AND [Status] <> 3;');
END;
