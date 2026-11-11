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
--    20261110201340_AddAppointmentStaffForeignKey. No debe aparecer nunca
--    20261110192224_AddAppointmentForeignKeys: fue un primer diseño de esta fase que no se publicó.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. La llave nueva y su índice. Antes de aplicar y después de rollback.sql no sale ninguna fila.
--    Después: [FK_Appointments_AspNetUsers_StaffUserId] (NO_ACTION, [Revisada] = 1) y el índice
--    filtrado [IX_Appointments_StaffUserId].
SELECT N'Llave' AS [Tipo], fk.[name] AS [Nombre], fk.[delete_referential_action_desc] AS [AlBorrar],
    CASE WHEN fk.[is_not_trusted] = 0 AND fk.[is_disabled] = 0 THEN 1 ELSE 0 END AS [Revisada], NULL AS [Filtro]
FROM sys.foreign_keys fk
WHERE fk.[parent_object_id] = OBJECT_ID(N'dbo.Appointments') AND fk.[name] = N'FK_Appointments_AspNetUsers_StaffUserId'
UNION ALL
SELECT N'Índice', i.[name], NULL, NULL, i.[filter_definition]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.Appointments') AND i.[name] = N'IX_Appointments_StaffUserId'
ORDER BY [Tipo], [Nombre];

-- 3. Antes (bloquea): citas cuya persona que atiende apunta a una cuenta que no existe. Si devuelve
--    filas, apply.sql se detiene con el error 50058 sin cambiar nada. Las cuentas no se borran, así
--    que no debería salir ninguna: si sale, ese Id es de otra cosa. Guarda la salida y revísalo antes
--    de seguir (quién la agendó y cuándo); si no apunta a nadie del equipo, vacíalo
--        UPDATE [Appointments] SET [StaffUserId] = NULL WHERE [Id] = <cita>;
--    La cita conserva el nombre de quien atiende en [StaffName] y la agenda la cruza por ese nombre.
--    Si es una cita en línea por venir, sin persona ocupa un lugar del cupo de las que no eligen con
--    quién: revisa ese día en la agenda.
SELECT t.[Slug] AS [Negocio], a.[Id] AS [Cita], a.[StaffName] AS [Atiende], a.[StaffUserId] AS [CuentaQueNoExiste],
    a.[Channel] AS [Canal], a.[StartAt] AS [Inicio], a.[Status] AS [Estado], a.[CreatedAt] AS [Creada]
FROM [Appointments] a
LEFT JOIN [Tenants] t ON t.[Id] = a.[TenantId]
WHERE a.[StaffUserId] IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = a.[StaffUserId])
ORDER BY t.[Slug], a.[StartAt];

-- 4. Antes y después (no bloquea): citas cuya persona no es del equipo de su negocio (no tiene
--    membresía en él). La llave solo exige que la cuenta exista; que sea del equipo lo revisa la app
--    al guardar, y una persona que dejó el equipo sigue en sus citas de antes. Solo informa.
SELECT t.[Slug] AS [Negocio], a.[Id] AS [Cita], a.[StaffName] AS [Atiende], a.[StartAt] AS [Inicio], a.[Status] AS [Estado]
FROM [Appointments] a
JOIN [AspNetUsers] u ON u.[Id] = a.[StaffUserId]
LEFT JOIN [Tenants] t ON t.[Id] = a.[TenantId]
WHERE NOT EXISTS (SELECT 1 FROM [TenantUsers] m WHERE m.[TenantId] = a.[TenantId] AND m.[UserId] = a.[StaffUserId])
ORDER BY t.[Slug], a.[StartAt];

-- 5. Antes y después: cuántas citas hay, cuántas tienen persona y cuántas lista la consulta 3. La
--    migración no cambia filas: [ConPersona] solo cambia por las citas que se agenden o editen entre
--    las dos corridas, y [CuentaQueNoExiste] es 0 después de aplicar (la llave ya no deja otra).
SELECT (SELECT COUNT(*) FROM [Appointments]) AS [Citas],
    (SELECT COUNT(*) FROM [Appointments] WHERE [StaffUserId] IS NOT NULL) AS [ConPersona],
    (SELECT COUNT(*) FROM [Appointments] a WHERE a.[StaffUserId] IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = a.[StaffUserId])) AS [CuentaQueNoExiste];
