-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte 20261004090406_AllowWalkInSalesWithoutCustomer.
-- Generado el 2026-10-04 con `dotnet ef migrations script
-- 20261004090406_AllowWalkInSalesWithoutCustomer 20261002094722_AddWhatsAppConversationVersion
-- --idempotent` (EF Core 9.0.0). Debajo del bloque SET, el contenido es el que genera EF.
--
-- Restaura la regla anterior de [CK_Orders_CustomerLink] (exactamente una referencia al
-- cliente) WITH NOCHECK: las ventas sin cliente registradas mientras la migración estuvo
-- aplicada son historial de ventas y se conservan; la regla vuelve a aplicar solo a filas
-- nuevas. La restricción queda marcada como no confiable (is_not_trusted = 1 en
-- verify.sql). Quita el registro en [__EFMigrationsHistory]. No borra ni modifica datos.
-- Idempotente: no hace nada si la migración no está aplicada.
--
-- Ejecútalo solo después de volver a desplegar el código anterior: ese código siempre
-- crea órdenes con una referencia, pero el de la Fase 0 registra ventas sin teléfono y
-- fallaría con la regla anterior.
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
    WHERE [MigrationId] = N'20261004090406_AllowWalkInSalesWithoutCustomer'
)
BEGIN
    ALTER TABLE [Orders] DROP CONSTRAINT [CK_Orders_CustomerLink];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090406_AllowWalkInSalesWithoutCustomer'
)
BEGIN
    ALTER TABLE [Orders] WITH NOCHECK ADD CONSTRAINT [CK_Orders_CustomerLink] CHECK ((([CustomerUserId] IS NOT NULL AND [TemporaryCustomerId] IS NULL) OR ([CustomerUserId] IS NULL AND [TemporaryCustomerId] IS NOT NULL)));
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090406_AllowWalkInSalesWithoutCustomer'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090406_AllowWalkInSalesWithoutCustomer';
END;

COMMIT;
GO
