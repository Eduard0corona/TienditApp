-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 31I, 20261111060433_AddOnlineBookingStaffAbsencesAndPhotos,
-- hasta la migración anterior, 20261110201340_AddAppointmentStaffForeignKey.
-- Generado el 2026-11-11 con `dotnet ef migrations script
-- 20261111060433_AddOnlineBookingStaffAbsencesAndPhotos 20261110201340_AddAppointmentStaffForeignKey --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Borra la tabla [OnlineBookingStaffAbsences] y la columna [OnlineBookingStaff].[PhotoUrl]. Antes
-- se detiene con el error 50059, sin cambiar nada, si algún negocio ya puso una ausencia o una
-- foto de quien atiende: se perderían. La consulta 3 de verify.sql las cuenta y dice cómo
-- revisarlas; solo si decides perderlas, bórralas antes (los archivos de las fotos se quedan en
-- el almacenamiento, en staff/<negocio>, y se pueden borrar a mano).
-- Córrelo después de regresar el código a master con la Fase 31H; volver a ese código funciona
-- también sin este script. Borrar la tabla toma el mismo candado de esquema sobre [Tenants] y
-- [OnlineBookingStaff] que crearla. Con -t 15, si tarda más, sqlcmd lo cancela sin cambiar nada.
-- Idempotente: no hace nada si la migración no está aplicada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -t 15 -i rollback.sql
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
    WHERE [MigrationId] = N'20261111060433_AddOnlineBookingStaffAbsencesAndPhotos'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [OnlineBookingStaffAbsences] WITH (TABLOCKX, HOLDLOCK))
        OR EXISTS (SELECT 1 FROM [OnlineBookingStaff] WITH (TABLOCKX, HOLDLOCK) WHERE [PhotoUrl] IS NOT NULL)
        THROW 50059, N''A business set an absence or a photo of who attends; rolling back would lose them (query 3 of verify.sql).'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111060433_AddOnlineBookingStaffAbsencesAndPhotos'
)
BEGIN
    DROP TABLE [OnlineBookingStaffAbsences];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111060433_AddOnlineBookingStaffAbsencesAndPhotos'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OnlineBookingStaff]') AND [c].[name] = N'PhotoUrl');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OnlineBookingStaff] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OnlineBookingStaff] DROP COLUMN [PhotoUrl];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111060433_AddOnlineBookingStaffAbsencesAndPhotos'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261111060433_AddOnlineBookingStaffAbsencesAndPhotos';
END;

COMMIT;
GO

