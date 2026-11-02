-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte la Fase 29H, 20261102205836_AddAppointmentProblemPhotos,
-- hasta la migración anterior, 20261102151555_AddQuoteOnlineReplies.
-- Generado el 2026-11-02 con `dotnet ef migrations script
-- 20261102205836_AddAppointmentProblemPhotos 20261102151555_AddQuoteOnlineReplies --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Se detiene sin cambiar nada (error 50056; con -b, sqlcmd termina ahí) en cuanto un cliente
-- mandó una foto de lo que pasa con su visita: se perdería cuál es de qué visita. Entonces solo
-- queda corregir hacia adelante (la consulta 3 de verify.sql las cuenta). Si no, borra la tabla.
-- Los archivos de las fotos no se borran: quedan en el almacenamiento privado del negocio.
-- La guarda bloquea la tabla hasta borrarla, en la misma transacción: ninguna foto se guarda entre
-- la guarda y el borrado (la que llegue espera y falla). Aun así, córrelo después de regresar el
-- código a master con la Fase 29G, que ya no recibe fotos. Volver al código anterior funciona
-- también sin este script.
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
    WHERE [MigrationId] = N'20261102205836_AddAppointmentProblemPhotos'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [AppointmentProblemPhotos] WITH (TABLOCKX, HOLDLOCK))
        THROW 50056, N''Customers already sent photos of what happens with their visits; rolling back would lose them. Roll forward instead.'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102205836_AddAppointmentProblemPhotos'
)
BEGIN
    DROP TABLE [AppointmentProblemPhotos];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102205836_AddAppointmentProblemPhotos'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102205836_AddAppointmentProblemPhotos';
END;

COMMIT;
GO
