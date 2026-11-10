-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 31E y las fases sin
--    migración que siguen, hasta la 31G), la más reciente es
--    20261110022936_AddUniqueCategoryNamePerTenant. Si es anterior, revisa primero el verify.sql
--    de su carpeta: apply.sql también aplica las que falten. Después:
--    20261110192224_AddAppointmentForeignKeys.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Las llaves nuevas de [Appointments] y sus índices. Antes de aplicar y después de rollback.sql
--    no sale ninguna fila. Después: [FK_Appointments_AspNetUsers_StaffUserId] (NO_ACTION) y
--    [FK_Appointments_Products_ServiceProductId] (SET_NULL), las dos con [Revisada] = 1, y los índices
--    filtrados [IX_Appointments_ServiceProductId] e [IX_Appointments_StaffUserId].
SELECT N'Llave' AS [Tipo], fk.[name] AS [Nombre], fk.[delete_referential_action_desc] AS [AlBorrar],
    CASE WHEN fk.[is_not_trusted] = 0 AND fk.[is_disabled] = 0 THEN 1 ELSE 0 END AS [Revisada], NULL AS [Filtro]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] = OBJECT_ID(N'dbo.Appointments')
    AND fk.[name] IN (N'FK_Appointments_AspNetUsers_StaffUserId', N'FK_Appointments_Products_ServiceProductId')
UNION ALL
SELECT N'Índice', i.[name], NULL, NULL, i.[filter_definition]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.Appointments')
    AND i.[name] IN (N'IX_Appointments_ServiceProductId', N'IX_Appointments_StaffUserId')
ORDER BY [Tipo], [Nombre];

-- 3. Antes (no bloquea): citas cuyo servicio apunta a un producto que ya no existe (se borró antes de
--    esta fase). apply.sql les vacía [ServiceProductId]; conservan el nombre del servicio. Después no
--    sale ninguna fila.
SELECT t.[Slug] AS [Negocio], a.[Id] AS [Cita], a.[ServiceName] AS [Servicio], a.[ServiceProductId] AS [ProductoQueNoExiste],
    a.[StartAt] AS [Inicio], a.[Status] AS [Estado]
FROM [Appointments] a
LEFT JOIN [Tenants] t ON t.[Id] = a.[TenantId]
WHERE a.[ServiceProductId] IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM [Products] p WHERE p.[Id] = a.[ServiceProductId])
ORDER BY t.[Slug], a.[StartAt];

-- 4. Antes (no bloquea): citas cuya persona que atiende apunta a una cuenta que no existe. apply.sql
--    les vacía [StaffUserId]; conservan el nombre en [StaffName]. Las cuentas no se borran, así que lo
--    esperado es que no salga ninguna fila. Después no sale ninguna.
SELECT t.[Slug] AS [Negocio], a.[Id] AS [Cita], a.[StaffName] AS [Atiende], a.[StaffUserId] AS [CuentaQueNoExiste],
    a.[StartAt] AS [Inicio], a.[Status] AS [Estado]
FROM [Appointments] a
LEFT JOIN [Tenants] t ON t.[Id] = a.[TenantId]
WHERE a.[StaffUserId] IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = a.[StaffUserId])
ORDER BY t.[Slug], a.[StartAt];

-- 5. Antes y después (no bloquea): citas con un servicio de otro negocio. La llave solo exige que el
--    producto exista; que sea del negocio lo revisa la app al guardar. Lo esperado es que no salga
--    ninguna fila; si sale, avisa: la migración no la cambia.
SELECT t.[Slug] AS [Negocio], a.[Id] AS [Cita], a.[ServiceName] AS [Servicio], p.[TenantId] AS [NegocioDelProducto]
FROM [Appointments] a
JOIN [Products] p ON p.[Id] = a.[ServiceProductId]
LEFT JOIN [Tenants] t ON t.[Id] = a.[TenantId]
WHERE p.[TenantId] <> a.[TenantId]
ORDER BY t.[Slug];

-- 6. Antes y después: cuántas citas hay, cuántas tienen cada vínculo y cuántas listan las consultas 3
--    y 4. Después de aplicar, [ServicioQueNoExiste] y [CuentaQueNoExiste] son 0 y las demás cuentas
--    bajan exactamente lo que esas dos tenían antes; [Citas] no cambia.
SELECT (SELECT COUNT(*) FROM [Appointments]) AS [Citas],
    (SELECT COUNT(*) FROM [Appointments] WHERE [ServiceProductId] IS NOT NULL) AS [ConServicio],
    (SELECT COUNT(*) FROM [Appointments] WHERE [StaffUserId] IS NOT NULL) AS [ConPersona],
    (SELECT COUNT(*) FROM [Appointments] a WHERE a.[ServiceProductId] IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM [Products] p WHERE p.[Id] = a.[ServiceProductId])) AS [ServicioQueNoExiste],
    (SELECT COUNT(*) FROM [Appointments] a WHERE a.[StaffUserId] IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = a.[StaffUserId])) AS [CuentaQueNoExiste];
