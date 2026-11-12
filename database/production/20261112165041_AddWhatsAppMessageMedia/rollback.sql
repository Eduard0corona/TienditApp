-- =====================================================================================
-- TienditApp (FoodParkDigital): revierte las Fases 31L, 31K y 31J (en ese orden),
-- 20261112165041_AddWhatsAppMessageMedia, 20261112161350_AddOnlineOrderReminderAndReviewEmails y
-- 20261112154235_AddAccountCartsAndPostalCode, hasta la migración anterior,
-- 20261111060433_AddOnlineBookingStaffAbsencesAndPhotos.
-- Generado el 2026-11-12 con `dotnet ef migrations script
-- 20261112165041_AddWhatsAppMessageMedia 20261111060433_AddOnlineBookingStaffAbsencesAndPhotos --idempotent`
-- (EF Core 10.0.12). Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Cada migración se revierte en su transacción:
--   - 31L: borra la restricción y las columnas de medios de [WhatsAppMessages]. Antes se detiene con
--     el error 50061, sin cambiar nada, si ya se guardó una foto o un audio de un cliente: se perdería
--     dónde está. La consulta 4 de verify.sql los cuenta. Los pendientes o no disponibles conservan
--     su marcador.
--   - 31K: borra [Orders].[ConfirmReminderSentAt] y [TenantUsers].[ReviewEmails]: se olvida quién
--     eligió los correos de reseñas y qué pedidos se recordaron; nada que escribió un cliente.
--   - 31J: borra la tabla [AccountCarts] y la columna [AspNetUsers].[PostalCode]. Antes se detiene con
--     el error 50060, sin cambiar nada de la 31J, si una cuenta ya dejó un carrito o su código postal.
--     La consulta 3 de verify.sql los cuenta. Si se detiene aquí, la 31L y la 31K ya se revirtieron:
--     la base queda en la 31J, que la versión anterior también ignora.
-- Solo si decides perder lo que cuentan las consultas 3 y 4, bórralo antes (los archivos de las fotos
-- y audios se quedan en el almacenamiento privado, en whatsapp/<negocio>).
-- Córrelo después de regresar el código a master con la Fase 31I; volver a ese código funciona
-- también sin este script. Toma los mismos candados de esquema que apply.sql. Con -t 15, si tarda
-- más, sqlcmd lo cancela sin cambiar nada de la migración en curso.
-- Idempotente: no hace nada con las migraciones que no están aplicadas.
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
    WHERE [MigrationId] = N'20261112165041_AddWhatsAppMessageMedia'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [WhatsAppMessages] WITH (TABLOCKX, HOLDLOCK) WHERE [MediaPath] IS NOT NULL)
        THROW 50061, N''A WhatsApp message keeps a photo or an audio a customer sent; rolling back would lose where it is (query 4 of verify.sql).'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112165041_AddWhatsAppMessageMedia'
)
BEGIN
    ALTER TABLE [WhatsAppMessages] DROP CONSTRAINT [CK_WhatsAppMessages_Media];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112165041_AddWhatsAppMessageMedia'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WhatsAppMessages]') AND [c].[name] = N'MediaId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [WhatsAppMessages] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [WhatsAppMessages] DROP COLUMN [MediaId];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112165041_AddWhatsAppMessageMedia'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WhatsAppMessages]') AND [c].[name] = N'MediaKind');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [WhatsAppMessages] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [WhatsAppMessages] DROP COLUMN [MediaKind];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112165041_AddWhatsAppMessageMedia'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WhatsAppMessages]') AND [c].[name] = N'MediaPath');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [WhatsAppMessages] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [WhatsAppMessages] DROP COLUMN [MediaPath];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112165041_AddWhatsAppMessageMedia'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WhatsAppMessages]') AND [c].[name] = N'MediaState');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [WhatsAppMessages] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [WhatsAppMessages] DROP COLUMN [MediaState];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112165041_AddWhatsAppMessageMedia'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112165041_AddWhatsAppMessageMedia';
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112161350_AddOnlineOrderReminderAndReviewEmails'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantUsers]') AND [c].[name] = N'ReviewEmails');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [TenantUsers] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [TenantUsers] DROP COLUMN [ReviewEmails];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112161350_AddOnlineOrderReminderAndReviewEmails'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'ConfirmReminderSentAt');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [Orders] DROP COLUMN [ConfirmReminderSentAt];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112161350_AddOnlineOrderReminderAndReviewEmails'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112161350_AddOnlineOrderReminderAndReviewEmails';
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112154235_AddAccountCartsAndPostalCode'
)
BEGIN

    EXEC(N'IF EXISTS (SELECT 1 FROM [AccountCarts] WITH (TABLOCKX, HOLDLOCK))
        OR EXISTS (SELECT 1 FROM [AspNetUsers] WITH (TABLOCKX, HOLDLOCK) WHERE [PostalCode] IS NOT NULL)
        THROW 50060, N''A customer account left a cart or a postal code; rolling back would lose them (query 3 of verify.sql).'', 1;');

END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112154235_AddAccountCartsAndPostalCode'
)
BEGIN
    DROP TABLE [AccountCarts];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112154235_AddAccountCartsAndPostalCode'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUsers]') AND [c].[name] = N'PostalCode');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUsers] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [AspNetUsers] DROP COLUMN [PostalCode];
END;

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112154235_AddAccountCartsAndPostalCode'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112154235_AddAccountCartsAndPostalCode';
END;

COMMIT;
GO
