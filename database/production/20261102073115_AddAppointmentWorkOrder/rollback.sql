-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29F, 20261102073115_AddAppointmentWorkOrder,
-- hasta la migración anterior, 20261102014702_AddQuoteConversion.
-- Generado el 2026-11-02 con `dotnet ef migrations script
-- 20261102073115_AddAppointmentWorkOrder 20261102014702_AddQuoteConversion --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50054; con -b, sqlcmd termina ahí) en cuanto una cita hace el
-- trabajo de una orden: se perdería de qué orden es y se podría cobrar otra vez. Entonces solo queda
-- corregir hacia adelante (la consulta 3 de verify.sql las cuenta). Si no, borra la llave, el
-- índice, la restricción y la columna.
-- Correrlo después de regresar el código a master con la Fase 29E: la guarda no bloquea la tabla,
-- y esta versión podría agendar el trabajo de una orden entre la guarda y el borrado. Volver al
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
    WHERE [MigrationId] = N'20261102073115_AddAppointmentWorkOrder'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [Appointments] WHERE [WorkOrderId] IS NOT NULL)
        THROW 50054, N''Appointments already do the job of service orders; rolling back would forget which order each one belongs to and let it be charged again. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073115_AddAppointmentWorkOrder'
)
BEGIN
    ALTER TABLE [Appointments] DROP CONSTRAINT [FK_Appointments_Orders_WorkOrderId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073115_AddAppointmentWorkOrder'
)
BEGIN
    DROP INDEX [IX_Appointments_WorkOrderId] ON [Appointments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073115_AddAppointmentWorkOrder'
)
BEGIN
    ALTER TABLE [Appointments] DROP CONSTRAINT [CK_Appointments_Work];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073115_AddAppointmentWorkOrder'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Appointments]') AND [c].[name] = N'WorkOrderId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Appointments] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Appointments] DROP COLUMN [WorkOrderId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073115_AddAppointmentWorkOrder'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073115_AddAppointmentWorkOrder';
END;

COMMIT;
GO
