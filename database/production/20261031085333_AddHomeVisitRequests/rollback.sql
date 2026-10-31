-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29A, 20261031085333_AddHomeVisitRequests,
-- hasta la migración anterior, 20261031053720_AddAccountantCfdiEmails.
-- Generado el 2026-10-31 con `dotnet ef migrations script
-- 20261031085333_AddHomeVisitRequests 20261031053720_AddAccountantCfdiEmails --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50049; con -b, sqlcmd termina ahí) si un dueño ya decidió
-- algo que se perdería: cambió cómo recibe visitas para cotizar (HomeVisitRequestsUpdated en
-- [AuditLogs]) o eligió «Hogar y reparaciones» en Explorar (categoría 11 con DirectoryProfileUpdated).
-- Entonces solo queda corregir hacia adelante (la consulta 4 de verify.sql los cuenta). Si no,
-- regresa los negocios de la categoría 11 a la que les daba su giro (los cinco oficios de antes a
-- Servicios, los demás por su tipo), quita las restricciones y las cuatro columnas nuevas de
-- [TenantSettings] (el interruptor que un oficio recibió al registrarse se pierde) y deja
-- [CK_Tenants_DirectoryCategory] de 0 a 10.
-- Correrlo después de regresar el código a master con la Fase 28O: la guarda no bloquea las
-- tablas, y esta versión podría guardar un cambio entre la guarda y el borrado. Volver al código
-- anterior funciona también sin este script.
-- Idempotente: no hace nada si la migración no está aplicada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -i rollback.sql
-- =====================================================================================
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN

    EXEC(N'IF EXISTS (
            SELECT 1 FROM [Tenants] AS t
            WHERE t.[DirectoryCategory] = 11 AND EXISTS (
                SELECT 1 FROM [AuditLogs] AS a
                WHERE a.[EntityName] = N''Tenant'' AND a.[Action] = N''DirectoryProfileUpdated''
                    AND a.[EntityId] = LOWER(CONVERT(nvarchar(36), t.[Id]))))
        OR EXISTS (SELECT 1 FROM [AuditLogs] WHERE [Action] = N''HomeVisitRequestsUpdated'')
        THROW 50049, N''Owners already decided their visits to quote or picked Hogar y reparaciones in Explorar; rolling back would lose it. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN

    EXEC(N'UPDATE t SET [DirectoryCategory] = CASE
        WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI IN (
            N''Carpinteria'', N''Herreria y soldadura'', N''Electricista'', N''Plomeria'', N''Aire acondicionado'') THEN 9
        WHEN t.[BusinessType] = 0 THEN 0
        WHEN t.[BusinessType] = 2 THEN 9
        ELSE 10
    END
    FROM [Tenants] AS t
    WHERE t.[DirectoryCategory] = 11;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN
    ALTER TABLE [TenantSettings] DROP CONSTRAINT [CK_TenantSettings_HomeVisitChargePolicy];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN
    ALTER TABLE [Tenants] DROP CONSTRAINT [CK_Tenants_DirectoryCategory];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'HomeVisitAddressNoticeDismissedAt');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [HomeVisitAddressNoticeDismissedAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'HomeVisitChargePolicy');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [HomeVisitChargePolicy];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'HomeVisitRequestsEnabled');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [HomeVisitRequestsEnabled];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'HomeVisitServiceId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [HomeVisitServiceId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN
    EXEC(N'ALTER TABLE [Tenants] ADD CONSTRAINT [CK_Tenants_DirectoryCategory] CHECK ([DirectoryCategory] >= 0 AND [DirectoryCategory] <= 10)');
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261031085333_AddHomeVisitRequests';
END;

COMMIT;
GO
