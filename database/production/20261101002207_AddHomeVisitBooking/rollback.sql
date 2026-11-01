-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29B, 20261101002207_AddHomeVisitBooking,
-- hasta la migración anterior, 20261031085333_AddHomeVisitRequests.
-- Generado el 2026-11-01 con `dotnet ef migrations script
-- 20261101002207_AddHomeVisitBooking 20261031085333_AddHomeVisitRequests --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50050; con -b, sqlcmd termina ahí) en cuanto un cliente pidió
-- una visita en línea: se perdería cómo se cobra, como la acordó, y a dónde va su cotización
-- ([ChargePolicy] o [CustomerEmail] con valor). Entonces solo queda corregir hacia adelante (la
-- consulta 3 de verify.sql los cuenta). Si no, quita la restricción y las dos columnas nuevas.
-- Correrlo después de regresar el código a master con la Fase 29A: la guarda no bloquea las
-- tablas, y esta versión podría guardar una solicitud entre la guarda y el borrado. Volver al
-- código anterior funciona también sin este script.
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
    WHERE [MigrationId] = N'20261101002207_AddHomeVisitBooking'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [AppointmentVisits] WHERE [ChargePolicy] IS NOT NULL)
        OR EXISTS (SELECT 1 FROM [Appointments] WHERE [CustomerEmail] IS NOT NULL)
        THROW 50050, N''Customers already requested visits to quote online; rolling back would lose how they are charged and where their quote goes. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101002207_AddHomeVisitBooking'
)
BEGIN
    ALTER TABLE [AppointmentVisits] DROP CONSTRAINT [CK_AppointmentVisits_ChargePolicy];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101002207_AddHomeVisitBooking'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AppointmentVisits]') AND [c].[name] = N'ChargePolicy');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [AppointmentVisits] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [AppointmentVisits] DROP COLUMN [ChargePolicy];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101002207_AddHomeVisitBooking'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'CustomerEmail');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Appointments] DROP COLUMN [CustomerEmail];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101002207_AddHomeVisitBooking'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261101002207_AddHomeVisitBooking';
END;

COMMIT;
GO

