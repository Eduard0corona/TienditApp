-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004163845_AddPlansAndSubscriptions.
-- Generado el 2026-10-04 con `dotnet ef migrations script
-- 20261004163845_AddPlansAndSubscriptions 20261004160303_AddBranchesAndDailySalesSnapshots
-- --idempotent` (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Borra Plans, TenantSubscriptions y SubscriptionPayments. Si hay pagos de planes registrados,
-- el script se detiene sin cambiar nada (error 50004): son historial financiero. Antes de
-- revertir, quita la suspensión de los negocios suspendidos por falta de pago (consulta 4 de
-- verify.sql): sin suscripciones nadie los volvería a activar.
-- Idempotente: no hace nada si la migración no está aplicada.
--
-- Ejecútalo solo después de volver a desplegar el código anterior, y revierte primero las
-- migraciones posteriores (cada carpeta tiene su rollback.sql), de la más nueva a la más vieja.
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
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN

    IF EXISTS (SELECT 1 FROM [SubscriptionPayments])
        THROW 50004, N'There are plan payments; rolling back would lose them.', 1;

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    DROP TABLE [SubscriptionPayments];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    DROP TABLE [TenantSubscriptions];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    DROP TABLE [Plans];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions';
END;

COMMIT;
GO
