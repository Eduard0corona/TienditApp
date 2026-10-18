-- =====================================================================================
-- TienditApp (FoodParkDigital): migraciones para producción (SQL Server / Azure SQL).
-- Generado el 2026-10-18 con `dotnet ef migrations script 0 20261018032200_AddBusinessCover --idempotent`
-- (EF Core 10.0.12) desde la rama claude/amazing-babbage-175ks9 (Fase 24E, sobre master con la Fase 24D-1).
-- Debajo del bloque SET, el contenido es el que genera EF, sin cambios.
--
-- Idempotente: cada migración se aplica solo si falta en [__EFMigrationsHistory]. En una base que
-- ya tiene 20261017225015_AddOnlineBooking (lo que exige master hoy) solo ejecuta
-- 20261018032200_AddBusinessCover, la portada del menú en línea (ADR 0026):
--   - agrega a [Tenants] la columna [CoverImageUrl], nvarchar(500) con nulos: la dirección base de
--     las variantes de la foto, como [LogoUrl]. Todos los negocios quedan sin portada (NULL) y su
--     menú muestra los colores del negocio hasta que el dueño suba una.
-- No cambia otros datos ni otras tablas.
-- Si a la base le faltan migraciones recientes, también las aplica (no sirve para crear una base
-- vacía): revisa antes el verify.sql de database/production/20261017225015_AddOnlineBooking.
--
-- Compatible con la versión desplegada (master con la Fase 24D-1): no conoce la columna, guarda
-- los negocios como siempre sin tocarla y /health/schema sigue sano. Volver a ella (redespliegue,
-- o swap si el slot de staging está activo) funciona sin rollback.sql: el menú deja de mostrar la
-- portada y nada más cambia.
--
-- Cada migración corre en su propia transacción (EF Core 10). XACT_ABORT ON revierte la
-- transacción ante cualquier error; las migraciones ya confirmadas se saltan si vuelves a
-- ejecutar el script. Las opciones SET son las que exigen los índices filtrados de migraciones
-- anteriores (el sqlcmd clásico arranca con QUOTED_IDENTIFIER OFF).
--
-- Orden: respaldo de la base -> verify.sql -> este script -> verify.sql -> fusionar la PR,
-- cuyo merge a master despliega a producción. Por ejemplo:
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql
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

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [Role] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EntityName] nvarchar(200) NOT NULL,
        [EntityId] nvarchar(50) NOT NULL,
        [Action] nvarchar(50) NOT NULL,
        [PerformedBy] nvarchar(256) NOT NULL,
        [Timestamp] datetimeoffset NOT NULL,
        [Metadata] nvarchar(max) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Description] nvarchar(500) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [Orders] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderNumber] nvarchar(20) NOT NULL,
        [BusinessType] int NOT NULL,
        [Status] int NOT NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        [CustomerPhone] nvarchar(20) NULL,
        [CustomerNotes] nvarchar(500) NULL,
        [TableNumber] nvarchar(10) NULL,
        [SubTotal] decimal(18,2) NOT NULL,
        [Tax] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [CancellationReason] nvarchar(500) NULL,
        [Version] rowversion NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Slug] nvarchar(100) NOT NULL,
        [BusinessType] int NOT NULL,
        [Status] int NOT NULL,
        [ContactEmail] nvarchar(256) NOT NULL,
        [ContactPhone] nvarchar(20) NULL,
        [LogoUrl] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] uniqueidentifier NOT NULL,
        [RoleId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] uniqueidentifier NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Token] nvarchar(500) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [ReplacedByToken] nvarchar(500) NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CategoryId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [PriceAmount] decimal(18,2) NOT NULL,
        [PriceCurrency] nvarchar(3) NOT NULL,
        [ImageUrl] nvarchar(500) NULL,
        [IsAvailable] bit NOT NULL,
        [PreparationTimeMinutes] int NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderItems] (
        [Id] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [TotalPrice] decimal(18,2) NOT NULL,
        [Notes] nvarchar(300) NULL,
        CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderStatusHistory] (
        [Id] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [PreviousStatus] int NOT NULL,
        [NewStatus] int NOT NULL,
        [ChangedAt] datetimeoffset NOT NULL,
        [ChangedBy] nvarchar(256) NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_OrderStatusHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderStatusHistory_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE TABLE [TenantSettings] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [TimeZone] nvarchar(50) NOT NULL,
        [AutoAcceptOrders] bit NOT NULL,
        [MaxConcurrentOrders] int NOT NULL,
        [SettingsJson] nvarchar(max) NULL,
        CONSTRAINT [PK_TenantSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantSettings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_TenantId] ON [AspNetUsers] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TenantId_EntityName_EntityId] ON [AuditLogs] ([TenantId], [EntityName], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TenantId_Timestamp] ON [AuditLogs] ([TenantId], [Timestamp] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Categories_TenantId] ON [Categories] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_OrderId] ON [OrderItems] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_TenantId_CreatedAt] ON [Orders] ([TenantId], [CreatedAt] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Orders_TenantId_OrderNumber] ON [Orders] ([TenantId], [OrderNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_TenantId_Status] ON [Orders] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderStatusHistory_OrderId_ChangedAt] ON [OrderStatusHistory] ([OrderId], [ChangedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_TenantId_CategoryId] ON [Products] ([TenantId], [CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_TenantId_IsAvailable] ON [Products] ([TenantId], [IsAvailable]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_Token] ON [RefreshTokens] ([Token]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Slug] ON [Tenants] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TenantSettings_TenantId] ON [TenantSettings] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260212085004_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260212085004_InitialCreate', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [Allergens] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [Barcode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [Calories] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [CompareAtPrice] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [IsFeatured] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [LowStockThreshold] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [MaxQuantityPerOrder] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [Sku] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [SortOrder] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [StockQuantity] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [Tags] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [TaxRate] decimal(5,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    ALTER TABLE [Products] ADD [Unit] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Products_TenantId_Barcode] ON [Products] ([TenantId], [Barcode]) WHERE [Barcode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    CREATE INDEX [IX_Products_TenantId_IsFeatured] ON [Products] ([TenantId], [IsFeatured]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Products_TenantId_Sku] ON [Products] ([TenantId], [Sku]) WHERE [Sku] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    CREATE INDEX [IX_Products_TenantId_SortOrder] ON [Products] ([TenantId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260213095915_AddProductCatalogProperties'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260213095915_AddProductCatalogProperties', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260214013146_AddUserAvatarUrl'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [AvatarUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260214013146_AddUserAvatarUrl'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260214013146_AddUserAvatarUrl', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address_City] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address_Country] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address_ExteriorNumber] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address_InteriorNumber] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address_Neighborhood] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address_State] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address_Street] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address_ZipCode] nvarchar(10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Industry] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [LegalName] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Rfc] nvarchar(13) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    ALTER TABLE [Tenants] ADD [TaxRegime] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215003348_AddBusinessFieldsAndCustomerRole'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215003348_AddBusinessFieldsAndCustomerRole', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215071141_AddWhatsAppConfig'
)
BEGIN
    CREATE TABLE [WhatsAppConfigs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsEnabled] bit NOT NULL DEFAULT CAST(0 AS bit),
        [AccessToken] nvarchar(500) NULL,
        [PhoneNumberId] varchar(50) NULL,
        [WabaId] varchar(50) NULL,
        [DisplayPhoneNumber] nvarchar(20) NULL,
        [VerifyToken] nvarchar(200) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_WhatsAppConfigs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WhatsAppConfigs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215071141_AddWhatsAppConfig'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WhatsAppConfigs_TenantId] ON [WhatsAppConfigs] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215071141_AddWhatsAppConfig'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215071141_AddWhatsAppConfig', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215083629_AddWhatsAppChat'
)
BEGIN
    CREATE TABLE [WhatsAppConversations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerPhone] varchar(20) NOT NULL,
        [CustomerName] nvarchar(200) NULL,
        [LastMessageBody] nvarchar(500) NULL,
        [LastMessageAt] datetimeoffset NOT NULL,
        [UnreadCount] int NOT NULL DEFAULT 0,
        [IsArchived] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_WhatsAppConversations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215083629_AddWhatsAppChat'
)
BEGIN
    CREATE TABLE [WhatsAppMessages] (
        [Id] uniqueidentifier NOT NULL,
        [ConversationId] uniqueidentifier NOT NULL,
        [Direction] int NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [ExternalMessageId] varchar(200) NULL,
        [SentAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_WhatsAppMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WhatsAppMessages_WhatsAppConversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [WhatsAppConversations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215083629_AddWhatsAppChat'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WhatsAppConversations_TenantId_CustomerPhone] ON [WhatsAppConversations] ([TenantId], [CustomerPhone]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215083629_AddWhatsAppChat'
)
BEGIN
    CREATE INDEX [IX_WhatsAppConversations_TenantId_LastMessageAt] ON [WhatsAppConversations] ([TenantId], [LastMessageAt] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215083629_AddWhatsAppChat'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessages_ConversationId_SentAt] ON [WhatsAppMessages] ([ConversationId], [SentAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215083629_AddWhatsAppChat'
)
BEGIN
    CREATE INDEX [IX_WhatsAppMessages_ExternalMessageId] ON [WhatsAppMessages] ([ExternalMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215083629_AddWhatsAppChat'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215083629_AddWhatsAppChat', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104438_AddCustomersAndOrderCustomer'
)
BEGIN
    ALTER TABLE [Orders] ADD [CustomerId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104438_AddCustomersAndOrderCustomer'
)
BEGIN
    CREATE TABLE [Customers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Phone] nvarchar(20) NULL,
        [Email] nvarchar(200) NULL,
        [IsPublicGeneral] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104438_AddCustomersAndOrderCustomer'
)
BEGIN
    CREATE INDEX [IX_Orders_CustomerId] ON [Orders] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104438_AddCustomersAndOrderCustomer'
)
BEGIN
    CREATE INDEX [IX_Orders_TenantId_CustomerId] ON [Orders] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104438_AddCustomersAndOrderCustomer'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Customers_TenantId_IsPublicGeneral] ON [Customers] ([TenantId], [IsPublicGeneral]) WHERE [IsPublicGeneral] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104438_AddCustomersAndOrderCustomer'
)
BEGIN
    CREATE INDEX [IX_Customers_TenantId_Name] ON [Customers] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104438_AddCustomersAndOrderCustomer'
)
BEGIN
    CREATE INDEX [IX_Customers_TenantId_Phone] ON [Customers] ([TenantId], [Phone]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104438_AddCustomersAndOrderCustomer'
)
BEGIN
    ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104438_AddCustomersAndOrderCustomer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215104438_AddCustomersAndOrderCustomer', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    ALTER TABLE [Orders] DROP CONSTRAINT [FK_Orders_Customers_CustomerId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    DROP INDEX [IX_AspNetUsers_TenantId] ON [AspNetUsers];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUsers]') AND [c].[name] = N'TenantId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [AspNetUsers] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [AspNetUsers] DROP COLUMN [TenantId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    EXEC sp_rename N'[Orders].[CustomerId]', N'TemporaryCustomerId', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    EXEC sp_rename N'[Orders].[IX_Orders_TenantId_CustomerId]', N'IX_Orders_TenantId_TemporaryCustomerId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    EXEC sp_rename N'[Orders].[IX_Orders_CustomerId]', N'IX_Orders_TemporaryCustomerId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    ALTER TABLE [Orders] ADD [CustomerUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [DefaultTenantId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE TABLE [CustomerProfiles] (
        [UserId] uniqueidentifier NOT NULL,
        [PhoneNormalized] nvarchar(20) NOT NULL,
        [DisplayName] nvarchar(200) NULL,
        [FirstName] nvarchar(100) NULL,
        [LastName] nvarchar(100) NULL,
        [LoyaltyPoints] bigint NOT NULL DEFAULT CAST(0 AS bigint),
        [LoyaltyTier] nvarchar(50) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_CustomerProfiles] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_CustomerProfiles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE TABLE [TemporaryCustomers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PhoneNormalized] nvarchar(20) NOT NULL,
        [Name] nvarchar(200) NULL,
        [Status] int NOT NULL DEFAULT 0,
        [ClaimedUserId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_TemporaryCustomers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TemporaryCustomers_AspNetUsers_ClaimedUserId] FOREIGN KEY ([ClaimedUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TemporaryCustomers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE TABLE [TenantUsers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Role] int NOT NULL,
        [Status] int NOT NULL DEFAULT 1,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_TenantUsers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantUsers_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TenantUsers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE INDEX [IX_Orders_CustomerUserId] ON [Orders] ([CustomerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE INDEX [IX_Orders_TenantId_CustomerUserId] ON [Orders] ([TenantId], [CustomerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    EXEC(N'ALTER TABLE [Orders] ADD CONSTRAINT [CK_Orders_CustomerLink] CHECK ((([CustomerUserId] IS NOT NULL AND [TemporaryCustomerId] IS NULL) OR ([CustomerUserId] IS NULL AND [TemporaryCustomerId] IS NOT NULL)))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_DefaultTenantId] ON [AspNetUsers] ([DefaultTenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CustomerProfiles_PhoneNormalized] ON [CustomerProfiles] ([PhoneNormalized]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE INDEX [IX_TemporaryCustomers_ClaimedUserId] ON [TemporaryCustomers] ([ClaimedUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE INDEX [IX_TemporaryCustomers_PhoneNormalized] ON [TemporaryCustomers] ([PhoneNormalized]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TemporaryCustomers_TenantId_PhoneNormalized] ON [TemporaryCustomers] ([TenantId], [PhoneNormalized]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE INDEX [IX_TenantUsers_TenantId] ON [TenantUsers] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TenantUsers_TenantId_UserId] ON [TenantUsers] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    CREATE INDEX [IX_TenantUsers_UserId] ON [TenantUsers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_AspNetUsers_CustomerUserId] FOREIGN KEY ([CustomerUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_TemporaryCustomers_TemporaryCustomerId] FOREIGN KEY ([TemporaryCustomerId]) REFERENCES [TemporaryCustomers] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215112943_AddTenantMembershipAndB2CModel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215112943_AddTenantMembershipAndB2CModel', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215123500_AddOrderCreatedByBusinessFlag'
)
BEGIN
    ALTER TABLE [Orders] ADD [CreatedByBusiness] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215123500_AddOrderCreatedByBusinessFlag'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215123500_AddOrderCreatedByBusinessFlag', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216010613_AddProductAvailabilitySchedule'
)
BEGIN
    ALTER TABLE [Products] ADD [AvailableFromTime] time NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216010613_AddProductAvailabilitySchedule'
)
BEGIN
    ALTER TABLE [Products] ADD [AvailableToTime] time NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216010613_AddProductAvailabilitySchedule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260216010613_AddProductAvailabilitySchedule', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216024310_AddAiAgents'
)
BEGIN
    CREATE TABLE [AiAgents] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Role] nvarchar(200) NOT NULL,
        [Personality] nvarchar(200) NOT NULL,
        [Language] nvarchar(50) NOT NULL,
        [Tone] nvarchar(50) NOT NULL,
        [BusinessName] nvarchar(200) NULL,
        [BusinessDescription] nvarchar(2000) NULL,
        [BusinessProductsAndServices] nvarchar(4000) NULL,
        [WelcomeMessage] nvarchar(2000) NOT NULL,
        [FallbackMessage] nvarchar(2000) NOT NULL,
        [ClosingMessage] nvarchar(2000) NOT NULL,
        [AskForName] bit NOT NULL,
        [AskForPhone] bit NOT NULL,
        [AskForEmail] bit NOT NULL,
        [AskForAddress] bit NOT NULL,
        [CustomQuestions] nvarchar(4000) NULL,
        [Model] nvarchar(100) NOT NULL,
        [Temperature] decimal(4,2) NOT NULL,
        [MaxTokens] int NOT NULL,
        [UseConversationMemory] bit NOT NULL,
        [StrictMode] bit NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_AiAgents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216024310_AddAiAgents'
)
BEGIN
    CREATE INDEX [IX_AiAgents_TenantId_IsActive] ON [AiAgents] ([TenantId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216024310_AddAiAgents'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AiAgents_TenantId_Name] ON [AiAgents] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216024310_AddAiAgents'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260216024310_AddAiAgents', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216200303_RemoveTenantUserStatusDefault'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantUsers]') AND [c].[name] = N'Status');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [TenantUsers] DROP CONSTRAINT ' + @var1 + ';');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216200303_RemoveTenantUserStatusDefault'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260216200303_RemoveTenantUserStatusDefault', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE TABLE [InventoryLocations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Description] nvarchar(500) NULL,
        [AddressLine1] nvarchar(250) NULL,
        [AddressLine2] nvarchar(250) NULL,
        [City] nvarchar(120) NULL,
        [State] nvarchar(120) NULL,
        [Country] nvarchar(120) NULL,
        [PostalCode] nvarchar(20) NULL,
        [ContactName] nvarchar(120) NULL,
        [ContactPhone] nvarchar(30) NULL,
        [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_InventoryLocations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE TABLE [InventoryMovements] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [MovementNumber] nvarchar(40) NOT NULL,
        [MovementType] int NOT NULL,
        [MovementReason] int NOT NULL,
        [MovementDate] datetimeoffset NOT NULL,
        [ReferenceNumber] nvarchar(100) NULL,
        [ExternalReference] nvarchar(100) NULL,
        [CounterpartyName] nvarchar(200) NULL,
        [DocumentNumber] nvarchar(100) NULL,
        [Notes] nvarchar(2000) NULL,
        [TotalAmount] decimal(18,4) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_InventoryMovements] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE TABLE [SupplyItems] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(100) NULL,
        [Sku] nvarchar(100) NULL,
        [Barcode] nvarchar(50) NULL,
        [Description] nvarchar(2000) NULL,
        [Brand] nvarchar(120) NULL,
        [Model] nvarchar(120) NULL,
        [CategoryName] nvarchar(120) NULL,
        [Unit] int NOT NULL,
        [DefaultCost] decimal(18,4) NOT NULL,
        [LastCost] decimal(18,4) NOT NULL,
        [AverageCost] decimal(18,4) NOT NULL,
        [MinStock] decimal(18,4) NOT NULL,
        [MaxStock] decimal(18,4) NULL,
        [ReorderPoint] decimal(18,4) NOT NULL,
        [TrackBatches] bit NOT NULL,
        [TrackExpiration] bit NOT NULL,
        [IsPerishable] bit NOT NULL,
        [TaxRate] decimal(5,2) NULL,
        [Notes] nvarchar(1000) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_SupplyItems] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE TABLE [InventoryMovementLines] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [MovementId] uniqueidentifier NOT NULL,
        [SupplyItemId] uniqueidentifier NOT NULL,
        [InventoryLocationId] uniqueidentifier NOT NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [UnitCost] decimal(18,4) NULL,
        [TotalCost] decimal(18,4) NULL,
        [QuantityBefore] decimal(18,4) NOT NULL,
        [QuantityAfter] decimal(18,4) NOT NULL,
        [LotNumber] nvarchar(80) NULL,
        [ExpirationDate] datetimeoffset NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_InventoryMovementLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryMovementLines_InventoryLocations_InventoryLocationId] FOREIGN KEY ([InventoryLocationId]) REFERENCES [InventoryLocations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryMovementLines_InventoryMovements_MovementId] FOREIGN KEY ([MovementId]) REFERENCES [InventoryMovements] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_InventoryMovementLines_SupplyItems_SupplyItemId] FOREIGN KEY ([SupplyItemId]) REFERENCES [SupplyItems] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE TABLE [InventoryStocks] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SupplyItemId] uniqueidentifier NOT NULL,
        [InventoryLocationId] uniqueidentifier NOT NULL,
        [OnHandQuantity] decimal(18,4) NOT NULL,
        [ReservedQuantity] decimal(18,4) NOT NULL,
        [LastCost] decimal(18,4) NOT NULL,
        [LastMovementAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_InventoryStocks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryStocks_InventoryLocations_InventoryLocationId] FOREIGN KEY ([InventoryLocationId]) REFERENCES [InventoryLocations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryStocks_SupplyItems_SupplyItemId] FOREIGN KEY ([SupplyItemId]) REFERENCES [SupplyItems] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InventoryLocations_TenantId_Code] ON [InventoryLocations] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryLocations_TenantId_IsActive] ON [InventoryLocations] ([TenantId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryLocations_TenantId_IsDefault] ON [InventoryLocations] ([TenantId], [IsDefault]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryLocations_TenantId_Name] ON [InventoryLocations] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryMovementLines_InventoryLocationId] ON [InventoryMovementLines] ([InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryMovementLines_MovementId] ON [InventoryMovementLines] ([MovementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryMovementLines_SupplyItemId] ON [InventoryMovementLines] ([SupplyItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryMovementLines_TenantId_ExpirationDate] ON [InventoryMovementLines] ([TenantId], [ExpirationDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryMovementLines_TenantId_MovementId] ON [InventoryMovementLines] ([TenantId], [MovementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryMovementLines_TenantId_SupplyItemId_InventoryLocationId] ON [InventoryMovementLines] ([TenantId], [SupplyItemId], [InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryMovements_TenantId_MovementDate] ON [InventoryMovements] ([TenantId], [MovementDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InventoryMovements_TenantId_MovementNumber] ON [InventoryMovements] ([TenantId], [MovementNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryMovements_TenantId_MovementType] ON [InventoryMovements] ([TenantId], [MovementType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryStocks_InventoryLocationId] ON [InventoryStocks] ([InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryStocks_SupplyItemId] ON [InventoryStocks] ([SupplyItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryStocks_TenantId_InventoryLocationId] ON [InventoryStocks] ([TenantId], [InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_InventoryStocks_TenantId_SupplyItemId] ON [InventoryStocks] ([TenantId], [SupplyItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InventoryStocks_TenantId_SupplyItemId_InventoryLocationId] ON [InventoryStocks] ([TenantId], [SupplyItemId], [InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_SupplyItems_TenantId_Barcode] ON [SupplyItems] ([TenantId], [Barcode]) WHERE [Barcode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SupplyItems_TenantId_Code] ON [SupplyItems] ([TenantId], [Code]) WHERE [Code] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_SupplyItems_TenantId_IsActive] ON [SupplyItems] ([TenantId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    CREATE INDEX [IX_SupplyItems_TenantId_Name] ON [SupplyItems] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SupplyItems_TenantId_Sku] ON [SupplyItems] ([TenantId], [Sku]) WHERE [Sku] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260216204705_AddInventoryManagement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260216204705_AddInventoryManagement', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [Capabilities] int NOT NULL DEFAULT 8;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    UPDATE ts
    SET ts.Capabilities =
        CASE t.BusinessType
            WHEN 0 THEN 44 -- Food + Inventory + Kitchen
            WHEN 1 THEN 9  -- Retail + Inventory
            WHEN 2 THEN 10 -- Service + Inventory
            ELSE 8         -- Inventory
        END
    FROM TenantSettings ts
    INNER JOIN Tenants t ON t.Id = ts.TenantId
    WHERE ts.Capabilities = 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    ALTER TABLE [Orders] ADD [OrderType] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    ALTER TABLE [Orders] ADD [PaidAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE TABLE [OrderPayments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Method] int NOT NULL,
        [PaidAt] datetimeoffset NOT NULL,
        [Reference] nvarchar(120) NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_OrderPayments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderPayments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE TABLE [ServiceOrderDetails] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [PromisedAt] datetimeoffset NULL,
        [Priority] int NOT NULL,
        [IntakeNotes] nvarchar(2000) NULL,
        [DeliveryNotes] nvarchar(2000) NULL,
        [IntakeChecklistJson] nvarchar(max) NULL,
        [BeforePhotosJson] nvarchar(max) NULL,
        [AfterPhotosJson] nvarchar(max) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_ServiceOrderDetails] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ServiceOrderDetails_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE TABLE [ServiceOrderPieces] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [ItemName] nvarchar(120) NOT NULL,
        [Brand] nvarchar(120) NULL,
        [Model] nvarchar(120) NULL,
        [Color] nvarchar(50) NULL,
        [Size] nvarchar(30) NULL,
        [Material] nvarchar(80) NULL,
        [ConditionNotes] nvarchar(1000) NULL,
        [RequestedService] nvarchar(300) NULL,
        [InternalNotes] nvarchar(1000) NULL,
        [Status] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_ServiceOrderPieces] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ServiceOrderPieces_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE INDEX [IX_Orders_TenantId_OrderType_Status] ON [Orders] ([TenantId], [OrderType], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE INDEX [IX_OrderPayments_OrderId] ON [OrderPayments] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE INDEX [IX_OrderPayments_TenantId_OrderId] ON [OrderPayments] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE INDEX [IX_OrderPayments_TenantId_PaidAt] ON [OrderPayments] ([TenantId], [PaidAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ServiceOrderDetails_OrderId] ON [ServiceOrderDetails] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE INDEX [IX_ServiceOrderDetails_TenantId_PromisedAt] ON [ServiceOrderDetails] ([TenantId], [PromisedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE INDEX [IX_ServiceOrderPieces_OrderId] ON [ServiceOrderPieces] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE INDEX [IX_ServiceOrderPieces_TenantId_OrderId] ON [ServiceOrderPieces] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    CREATE INDEX [IX_ServiceOrderPieces_TenantId_Status] ON [ServiceOrderPieces] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217004713_AddOrderServiceAndTenantCapabilities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260217004713_AddOrderServiceAndTenantCapabilities', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217015139_BackfillLegacyTenantCapabilities'
)
BEGIN
    UPDATE ts
    SET ts.Capabilities =
        CASE t.BusinessType
            WHEN 0 THEN 44 -- Food + Inventory + Kitchen
            WHEN 1 THEN 9  -- Retail + Inventory
            WHEN 2 THEN 10 -- Service + Inventory
            ELSE ts.Capabilities
        END
    FROM TenantSettings ts
    INNER JOIN Tenants t ON t.Id = ts.TenantId
    WHERE ts.Capabilities IN (0, 8);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217015139_BackfillLegacyTenantCapabilities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260217015139_BackfillLegacyTenantCapabilities', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    CREATE TABLE [ProductSupplyRequirements] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [SupplyItemId] uniqueidentifier NOT NULL,
        [InventoryLocationId] uniqueidentifier NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [WastePercent] decimal(5,2) NOT NULL,
        [Notes] nvarchar(500) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_ProductSupplyRequirements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductSupplyRequirements_InventoryLocations_InventoryLocationId] FOREIGN KEY ([InventoryLocationId]) REFERENCES [InventoryLocations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductSupplyRequirements_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProductSupplyRequirements_SupplyItems_SupplyItemId] FOREIGN KEY ([SupplyItemId]) REFERENCES [SupplyItems] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_InventoryMovements_TenantId_ExternalReference] ON [InventoryMovements] ([TenantId], [ExternalReference]) WHERE [ExternalReference] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    CREATE INDEX [IX_ProductSupplyRequirements_InventoryLocationId] ON [ProductSupplyRequirements] ([InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    CREATE INDEX [IX_ProductSupplyRequirements_ProductId] ON [ProductSupplyRequirements] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    CREATE INDEX [IX_ProductSupplyRequirements_SupplyItemId] ON [ProductSupplyRequirements] ([SupplyItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    CREATE INDEX [IX_ProductSupplyRequirements_TenantId_InventoryLocationId] ON [ProductSupplyRequirements] ([TenantId], [InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    CREATE INDEX [IX_ProductSupplyRequirements_TenantId_ProductId] ON [ProductSupplyRequirements] ([TenantId], [ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductSupplyRequirements_TenantId_ProductId_SupplyItemId_InventoryLocationId] ON [ProductSupplyRequirements] ([TenantId], [ProductId], [SupplyItemId], [InventoryLocationId]) WHERE [InventoryLocationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_ProductSupplyRequirements_TenantId_ProductId_SupplyItemId_IsActive] ON [ProductSupplyRequirements] ([TenantId], [ProductId], [SupplyItemId], [IsActive]) WHERE [InventoryLocationId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    CREATE INDEX [IX_ProductSupplyRequirements_TenantId_SupplyItemId] ON [ProductSupplyRequirements] ([TenantId], [SupplyItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217021053_AddProductRecipesAndInventoryExternalReference'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260217021053_AddProductRecipesAndInventoryExternalReference', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE TABLE [Appointments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        [CustomerPhone] nvarchar(50) NOT NULL,
        [ServiceName] nvarchar(200) NOT NULL,
        [StaffName] nvarchar(120) NULL,
        [StartAt] datetimeoffset NOT NULL,
        [EndAt] datetimeoffset NOT NULL,
        [Status] int NOT NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Appointments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE TABLE [CashSessions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OpenedAt] datetimeoffset NOT NULL,
        [OpenedBy] nvarchar(200) NOT NULL,
        [OpeningAmount] decimal(18,4) NOT NULL,
        [ClosedAt] datetimeoffset NULL,
        [ClosedBy] nvarchar(200) NULL,
        [ClosingAmount] decimal(18,4) NULL,
        [Status] int NOT NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_CashSessions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE TABLE [Suppliers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [ContactName] nvarchar(200) NULL,
        [Phone] nvarchar(50) NULL,
        [Email] nvarchar(200) NULL,
        [TaxId] nvarchar(80) NULL,
        [Address] nvarchar(500) NULL,
        [Notes] nvarchar(2000) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Suppliers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE TABLE [CashTransactions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CashSessionId] uniqueidentifier NOT NULL,
        [TransactionType] int NOT NULL,
        [Amount] decimal(18,4) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [PaymentMethod] int NULL,
        [OrderId] uniqueidentifier NULL,
        [Reference] nvarchar(120) NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_CashTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CashTransactions_CashSessions_CashSessionId] FOREIGN KEY ([CashSessionId]) REFERENCES [CashSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE TABLE [PurchaseOrders] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SupplierId] uniqueidentifier NOT NULL,
        [OrderNumber] nvarchar(40) NOT NULL,
        [Status] int NOT NULL,
        [OrderedAt] datetimeoffset NOT NULL,
        [ExpectedDeliveryAt] datetimeoffset NULL,
        [ReceivedAt] datetimeoffset NULL,
        [ReferenceNumber] nvarchar(100) NULL,
        [Notes] nvarchar(2000) NULL,
        [SubTotal] decimal(18,4) NOT NULL,
        [Tax] decimal(18,4) NOT NULL,
        [Total] decimal(18,4) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_PurchaseOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrders_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE TABLE [PurchaseOrderLines] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PurchaseOrderId] uniqueidentifier NOT NULL,
        [SupplyItemId] uniqueidentifier NOT NULL,
        [InventoryLocationId] uniqueidentifier NOT NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [ReceivedQuantity] decimal(18,4) NOT NULL,
        [UnitCost] decimal(18,4) NOT NULL,
        [TaxPercent] decimal(5,2) NOT NULL,
        [SubTotal] decimal(18,4) NOT NULL,
        [TaxAmount] decimal(18,4) NOT NULL,
        [Total] decimal(18,4) NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_PurchaseOrderLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrderLines_InventoryLocations_InventoryLocationId] FOREIGN KEY ([InventoryLocationId]) REFERENCES [InventoryLocations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [PurchaseOrders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PurchaseOrderLines_SupplyItems_SupplyItemId] FOREIGN KEY ([SupplyItemId]) REFERENCES [SupplyItems] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_Appointments_TenantId_CustomerPhone] ON [Appointments] ([TenantId], [CustomerPhone]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_Appointments_TenantId_StartAt] ON [Appointments] ([TenantId], [StartAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_Appointments_TenantId_Status] ON [Appointments] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_CashSessions_TenantId_OpenedAt] ON [CashSessions] ([TenantId], [OpenedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_CashSessions_TenantId_Status] ON [CashSessions] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_CashTransactions_CashSessionId] ON [CashTransactions] ([CashSessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_CashTransactions_TenantId_CashSessionId] ON [CashTransactions] ([TenantId], [CashSessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_CashTransactions_TenantId_OccurredAt] ON [CashTransactions] ([TenantId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_CashTransactions_TenantId_OrderId] ON [CashTransactions] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_CashTransactions_TenantId_TransactionType] ON [CashTransactions] ([TenantId], [TransactionType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_InventoryLocationId] ON [PurchaseOrderLines] ([InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_PurchaseOrderId] ON [PurchaseOrderLines] ([PurchaseOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_SupplyItemId] ON [PurchaseOrderLines] ([SupplyItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_TenantId_InventoryLocationId] ON [PurchaseOrderLines] ([TenantId], [InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_TenantId_PurchaseOrderId] ON [PurchaseOrderLines] ([TenantId], [PurchaseOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_TenantId_SupplyItemId] ON [PurchaseOrderLines] ([TenantId], [SupplyItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_SupplierId] ON [PurchaseOrders] ([SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseOrders_TenantId_OrderNumber] ON [PurchaseOrders] ([TenantId], [OrderNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_TenantId_Status] ON [PurchaseOrders] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_TenantId_SupplierId] ON [PurchaseOrders] ([TenantId], [SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_Suppliers_TenantId_IsActive] ON [Suppliers] ([TenantId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    CREATE INDEX [IX_Suppliers_TenantId_Name] ON [Suppliers] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217024339_AddPurchasingCashAndAppointments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260217024339_AddPurchasingCashAndAppointments', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217074419_AddOwnerWhatsAppConfig'
)
BEGIN
    CREATE TABLE [OwnerWhatsAppConfigs] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [IsEnabled] bit NOT NULL DEFAULT CAST(0 AS bit),
        [UseForOtp] bit NOT NULL DEFAULT CAST(1 AS bit),
        [AccessToken] nvarchar(500) NULL,
        [PhoneNumberId] varchar(50) NULL,
        [WabaId] varchar(50) NULL,
        [DisplayPhoneNumber] nvarchar(20) NULL,
        [OtpTemplateName] nvarchar(100) NOT NULL DEFAULT N'otp_verification',
        [OtpTemplateLanguageCode] nvarchar(10) NOT NULL DEFAULT N'es_MX',
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_OwnerWhatsAppConfigs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OwnerWhatsAppConfigs_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217074419_AddOwnerWhatsAppConfig'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OwnerWhatsAppConfigs_UserId] ON [OwnerWhatsAppConfigs] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217074419_AddOwnerWhatsAppConfig'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260217074419_AddOwnerWhatsAppConfig', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218081942_EnableRetailForServiceTenants'
)
BEGIN
    UPDATE ts
    SET ts.Capabilities = ts.Capabilities | 1 -- Retail
    FROM TenantSettings ts
    INNER JOIN Tenants t ON t.Id = ts.TenantId
    WHERE t.BusinessType = 2                 -- Service
      AND (ts.Capabilities & 2) = 2          -- Has Service
      AND (ts.Capabilities & 1) = 0;         -- Missing Retail
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218081942_EnableRetailForServiceTenants'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218081942_EnableRetailForServiceTenants', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218102813_AddServiceTrackingChecklist'
)
BEGIN
    ALTER TABLE [ServiceOrderPieces] ADD [IsTrackable] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218102813_AddServiceTrackingChecklist'
)
BEGIN
    ALTER TABLE [ServiceOrderPieces] ADD [NotifyCustomer] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218102813_AddServiceTrackingChecklist'
)
BEGIN
    ALTER TABLE [ServiceOrderPieces] ADD [ServiceProductId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218102813_AddServiceTrackingChecklist'
)
BEGIN
    ALTER TABLE [Products] ADD [IsTrackable] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218102813_AddServiceTrackingChecklist'
)
BEGIN
    ALTER TABLE [Products] ADD [NotifyCustomer] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218102813_AddServiceTrackingChecklist'
)
BEGIN
    CREATE INDEX [IX_ServiceOrderPieces_TenantId_IsTrackable] ON [ServiceOrderPieces] ([TenantId], [IsTrackable]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218102813_AddServiceTrackingChecklist'
)
BEGIN
    UPDATE ServiceOrderPieces
    SET Status = CASE
        WHEN Status = 0 THEN 0          -- Received -> Accepted
        WHEN Status IN (1,2,3,4,5) THEN 1 -- Inspection/Washing/... -> InProgress
        WHEN Status = 6 THEN 2          -- Delivered -> Completed
        WHEN Status = 99 THEN 99        -- Cancelled
        ELSE 0
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218102813_AddServiceTrackingChecklist'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218102813_AddServiceTrackingChecklist', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219080200_AddServiceBoardAssignment'
)
BEGIN
    ALTER TABLE [ServiceOrderPieces] ADD [AssignedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219080200_AddServiceBoardAssignment'
)
BEGIN
    ALTER TABLE [ServiceOrderPieces] ADD [AssignedToName] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219080200_AddServiceBoardAssignment'
)
BEGIN
    ALTER TABLE [ServiceOrderPieces] ADD [AssignedToUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219080200_AddServiceBoardAssignment'
)
BEGIN
    CREATE INDEX [IX_ServiceOrderPieces_AssignedToUserId] ON [ServiceOrderPieces] ([AssignedToUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219080200_AddServiceBoardAssignment'
)
BEGIN
    CREATE INDEX [IX_ServiceOrderPieces_TenantId_AssignedToUserId_Status] ON [ServiceOrderPieces] ([TenantId], [AssignedToUserId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219080200_AddServiceBoardAssignment'
)
BEGIN
    ALTER TABLE [ServiceOrderPieces] ADD CONSTRAINT [FK_ServiceOrderPieces_AspNetUsers_AssignedToUserId] FOREIGN KEY ([AssignedToUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219080200_AddServiceBoardAssignment'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260219080200_AddServiceBoardAssignment', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219095434_AddCriticalSupplyAlerts'
)
BEGIN
    ALTER TABLE [SupplyItems] ADD [IsCritical] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219095434_AddCriticalSupplyAlerts'
)
BEGIN
    CREATE INDEX [IX_SupplyItems_TenantId_IsCritical] ON [SupplyItems] ([TenantId], [IsCritical]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219095434_AddCriticalSupplyAlerts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260219095434_AddCriticalSupplyAlerts', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    ALTER TABLE [OrderPayments] ADD [EvidenceUrl] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    ALTER TABLE [OrderPayments] ADD [InventoryLocationId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    ALTER TABLE [CashTransactions] ADD [InventoryLocationId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    CREATE INDEX [IX_OrderPayments_InventoryLocationId] ON [OrderPayments] ([InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    CREATE INDEX [IX_OrderPayments_TenantId_InventoryLocationId_PaidAt] ON [OrderPayments] ([TenantId], [InventoryLocationId], [PaidAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    CREATE INDEX [IX_CashTransactions_InventoryLocationId] ON [CashTransactions] ([InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    CREATE INDEX [IX_CashTransactions_TenantId_InventoryLocationId_OccurredAt] ON [CashTransactions] ([TenantId], [InventoryLocationId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    ALTER TABLE [CashTransactions] ADD CONSTRAINT [FK_CashTransactions_InventoryLocations_InventoryLocationId] FOREIGN KEY ([InventoryLocationId]) REFERENCES [InventoryLocations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    ALTER TABLE [OrderPayments] ADD CONSTRAINT [FK_OrderPayments_InventoryLocations_InventoryLocationId] FOREIGN KEY ([InventoryLocationId]) REFERENCES [InventoryLocations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219104741_AddPaymentLocationAndEvidence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260219104741_AddPaymentLocationAndEvidence', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219113445_AddTenantUserPermissionCodes'
)
BEGIN
    ALTER TABLE [TenantUsers] ADD [PermissionCodes] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260219113445_AddTenantUserPermissionCodes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260219113445_AddTenantUserPermissionCodes', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001155915_AddWhatsAppWebhookUniqueIndexes'
)
BEGIN
    UPDATE [WhatsAppConfigs] SET [PhoneNumberId] = NULL WHERE LTRIM(RTRIM([PhoneNumberId])) = '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001155915_AddWhatsAppWebhookUniqueIndexes'
)
BEGIN
    DROP INDEX [IX_WhatsAppMessages_ExternalMessageId] ON [WhatsAppMessages];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001155915_AddWhatsAppWebhookUniqueIndexes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WhatsAppMessages_ExternalMessageId] ON [WhatsAppMessages] ([ExternalMessageId]) WHERE [ExternalMessageId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001155915_AddWhatsAppWebhookUniqueIndexes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WhatsAppConfigs_PhoneNumberId] ON [WhatsAppConfigs] ([PhoneNumberId]) WHERE [PhoneNumberId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001155915_AddWhatsAppWebhookUniqueIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001155915_AddWhatsAppWebhookUniqueIndexes', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001161600_CashSessionSingleOpenAndRevision'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [Revision] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001161600_CashSessionSingleOpenAndRevision'
)
BEGIN
    WITH Ranked AS (
        SELECT Id,
               ROW_NUMBER() OVER (PARTITION BY TenantId ORDER BY OpenedAt DESC, Id DESC) AS RowNumber
        FROM CashSessions
        WHERE Status = 0
    )
    UPDATE cs
    SET Status = 1,
        ClosedAt = SYSDATETIMEOFFSET(),
        ClosedBy = N'system-migration',
        Notes = LEFT(CONCAT(COALESCE(cs.Notes + N' | ', N''),
            N'Cerrada automaticamente al migrar: el negocio tenia otra caja abierta.'), 2000)
    FROM CashSessions AS cs
    INNER JOIN Ranked AS r ON r.Id = cs.Id
    WHERE r.RowNumber > 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001161600_CashSessionSingleOpenAndRevision'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CashSessions_TenantId_SingleOpen] ON [CashSessions] ([TenantId]) WHERE [Status] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001161600_CashSessionSingleOpenAndRevision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001161600_CashSessionSingleOpenAndRevision', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001163347_InventoryConcurrencyTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001163347_InventoryConcurrencyTokens', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002094722_AddWhatsAppConversationVersion'
)
BEGIN
    ALTER TABLE [WhatsAppConversations] ADD [Version] rowversion NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002094722_AddWhatsAppConversationVersion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002094722_AddWhatsAppConversationVersion', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090406_AllowWalkInSalesWithoutCustomer'
)
BEGIN
    ALTER TABLE [Orders] DROP CONSTRAINT [CK_Orders_CustomerLink];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090406_AllowWalkInSalesWithoutCustomer'
)
BEGIN
    EXEC(N'ALTER TABLE [Orders] ADD CONSTRAINT [CK_Orders_CustomerLink] CHECK (([CustomerUserId] IS NULL OR [TemporaryCustomerId] IS NULL) AND ([CreatedByBusiness] = 1 OR [CustomerUserId] IS NOT NULL OR [TemporaryCustomerId] IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004090406_AllowWalkInSalesWithoutCustomer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004090406_AllowWalkInSalesWithoutCustomer', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [AutoCloseCounterSales] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [DefaultTaxRate] decimal(5,2) NOT NULL DEFAULT 16.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [OnboardingCompletedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [PricesIncludeTax] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    ALTER TABLE [Products] ADD [IsTaxExempt] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    ALTER TABLE [Orders] ADD [PricesIncludeTax] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [IsTaxExempt] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [TaxRate] decimal(5,2) NOT NULL DEFAULT 16.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    EXEC(N'UPDATE [ts]
    SET [OnboardingCompletedAt] = SYSDATETIMEOFFSET()
    FROM [TenantSettings] AS [ts]
    WHERE [ts].[OnboardingCompletedAt] IS NULL
      AND (EXISTS (SELECT 1 FROM [Products] AS [p] WHERE [p].[TenantId] = [ts].[TenantId])
        OR EXISTS (SELECT 1 FROM [Orders] AS [o] WHERE [o].[TenantId] = [ts].[TenantId]))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004105827_AddTaxPolicyAndCounterSaleClosing'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004105827_AddTaxPolicyAndCounterSaleClosing', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    CREATE TABLE [InboxMessages] (
        [Id] uniqueidentifier NOT NULL,
        [Source] varchar(50) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [PayloadHash] char(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [NextAttemptAt] datetimeoffset NOT NULL,
        [Attempts] int NOT NULL,
        [ProcessedAt] datetimeoffset NULL,
        [DeadLetteredAt] datetimeoffset NULL,
        [LockedUntil] datetimeoffset NULL,
        [LockToken] uniqueidentifier NOT NULL,
        [LastError] nvarchar(1000) NULL,
        CONSTRAINT [PK_InboxMessages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    CREATE TABLE [OutboxMessages] (
        [Id] uniqueidentifier NOT NULL,
        [Type] varchar(300) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [TenantId] uniqueidentifier NULL,
        [ActorUserId] nvarchar(100) NULL,
        [ActorName] nvarchar(256) NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [NextAttemptAt] datetimeoffset NOT NULL,
        [Attempts] int NOT NULL,
        [ProcessedAt] datetimeoffset NULL,
        [DeadLetteredAt] datetimeoffset NULL,
        [LockedUntil] datetimeoffset NULL,
        [LockToken] uniqueidentifier NOT NULL,
        [LastError] nvarchar(1000) NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    CREATE TABLE [OutboxMessageConsumers] (
        [MessageId] uniqueidentifier NOT NULL,
        [HandlerName] varchar(300) NOT NULL,
        [ProcessedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_OutboxMessageConsumers] PRIMARY KEY ([MessageId], [HandlerName]),
        CONSTRAINT [FK_OutboxMessageConsumers_OutboxMessages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [OutboxMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_InboxMessages_Pending] ON [InboxMessages] ([NextAttemptAt], [CreatedAt]) WHERE [ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_InboxMessages_ProcessedAt] ON [InboxMessages] ([ProcessedAt]) WHERE [ProcessedAt] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InboxMessages_Source_PayloadHash] ON [InboxMessages] ([Source], [PayloadHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_OutboxMessages_Pending] ON [OutboxMessages] ([NextAttemptAt], [CreatedAt]) WHERE [ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_OutboxMessages_ProcessedAt] ON [OutboxMessages] ([ProcessedAt]) WHERE [ProcessedAt] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004121539_AddOutboxAndWebhookInbox'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004121539_AddOutboxAndWebhookInbox', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    DROP INDEX [IX_CashSessions_TenantId_SingleOpen] ON [CashSessions];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [RequireOpenCashSession] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [CashCountJson] nvarchar(4000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [ClosingDifference] decimal(18,4) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [ClosingNotes] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [ExpectedClosingAmount] decimal(18,4) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [InventoryLocationId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [OpenedByUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    EXEC(N'UPDATE [cs]
    SET [InventoryLocationId] = (
        SELECT TOP (1) [il].[Id]
        FROM [InventoryLocations] AS [il]
        WHERE [il].[TenantId] = [cs].[TenantId] AND [il].[IsActive] = 1
        ORDER BY [il].[IsDefault] DESC, [il].[Name])
    FROM [CashSessions] AS [cs]
    WHERE [cs].[InventoryLocationId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    EXEC(N'UPDATE [cs]
    SET [OpenedByUserId] = [u].[Id]
    FROM [CashSessions] AS [cs]
    INNER JOIN [AspNetUsers] AS [u] ON [u].[NormalizedEmail] = UPPER([cs].[OpenedBy])
    INNER JOIN [TenantUsers] AS [tu] ON [tu].[UserId] = [u].[Id] AND [tu].[TenantId] = [cs].[TenantId]
    WHERE [cs].[OpenedByUserId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    EXEC(N'UPDATE [cs]
    SET [ExpectedClosingAmount] = [cs].[OpeningAmount] + ISNULL((
        SELECT SUM([t].[Amount])
        FROM [CashTransactions] AS [t]
        WHERE [t].[CashSessionId] = [cs].[Id]
          AND ([t].[PaymentMethod] IS NULL OR [t].[PaymentMethod] = 0)), 0)
    FROM [CashSessions] AS [cs]
    WHERE [cs].[Status] = 1 AND [cs].[ExpectedClosingAmount] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    EXEC(N'UPDATE [CashSessions]
    SET [ClosingDifference] = [ClosingAmount] - [ExpectedClosingAmount]
    WHERE [Status] = 1
      AND [ClosingAmount] IS NOT NULL
      AND [ExpectedClosingAmount] IS NOT NULL
      AND [ClosingDifference] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    CREATE INDEX [IX_CashSessions_InventoryLocationId] ON [CashSessions] ([InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    CREATE INDEX [IX_CashSessions_TenantId_InventoryLocationId_OpenedAt] ON [CashSessions] ([TenantId], [InventoryLocationId], [OpenedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CashSessions_TenantId_OpenedByUserId_Open] ON [CashSessions] ([TenantId], [OpenedByUserId]) WHERE [Status] = 0 AND [OpenedByUserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    ALTER TABLE [CashSessions] ADD CONSTRAINT [FK_CashSessions_InventoryLocations_InventoryLocationId] FOREIGN KEY ([InventoryLocationId]) REFERENCES [InventoryLocations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004123654_CashSessionsPerCashier'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004123654_CashSessionsPerCashier', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    ALTER TABLE [Orders] ADD [RefundedAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [ReturnedQuantity] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    CREATE TABLE [OrderRefunds] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Method] int NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [RefundedAt] datetimeoffset NOT NULL,
        [RefundedBy] nvarchar(256) NOT NULL,
        [InventoryLocationId] uniqueidentifier NULL,
        [Reference] nvarchar(120) NULL,
        [RestockItems] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_OrderRefunds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderRefunds_InventoryLocations_InventoryLocationId] FOREIGN KEY ([InventoryLocationId]) REFERENCES [InventoryLocations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderRefunds_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    CREATE TABLE [OrderRefundLines] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RefundId] uniqueidentifier NOT NULL,
        [OrderItemId] uniqueidentifier NOT NULL,
        [Quantity] int NOT NULL,
        CONSTRAINT [PK_OrderRefundLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderRefundLines_OrderItems_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems] ([Id]),
        CONSTRAINT [FK_OrderRefundLines_OrderRefunds_RefundId] FOREIGN KEY ([RefundId]) REFERENCES [OrderRefunds] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    CREATE INDEX [IX_OrderRefundLines_OrderItemId] ON [OrderRefundLines] ([OrderItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    CREATE INDEX [IX_OrderRefundLines_RefundId] ON [OrderRefundLines] ([RefundId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    CREATE INDEX [IX_OrderRefundLines_TenantId_RefundId] ON [OrderRefundLines] ([TenantId], [RefundId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    CREATE INDEX [IX_OrderRefunds_InventoryLocationId] ON [OrderRefunds] ([InventoryLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    CREATE INDEX [IX_OrderRefunds_OrderId] ON [OrderRefunds] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    CREATE INDEX [IX_OrderRefunds_TenantId_OrderId] ON [OrderRefunds] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    CREATE INDEX [IX_OrderRefunds_TenantId_RefundedAt] ON [OrderRefunds] ([TenantId], [RefundedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004125133_AddOrderRefunds'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004125133_AddOrderRefunds', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [AppointmentReminderHoursBefore] int NOT NULL DEFAULT 24;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [AppointmentReminderTemplateId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [AppointmentRemindersEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE TABLE [MessageTemplates] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Purpose] int NOT NULL,
        [Body] nvarchar(1000) NOT NULL,
        [ProviderTemplateName] varchar(100) NULL,
        [LanguageCode] varchar(10) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_MessageTemplates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE TABLE [MessagingOptOuts] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Phone] varchar(20) NOT NULL,
        [Source] int NOT NULL,
        [OptedOutAt] datetimeoffset NOT NULL,
        [ResubscribedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_MessagingOptOuts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE TABLE [MessageCampaigns] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [TemplateId] uniqueidentifier NOT NULL,
        [AudienceKind] int NOT NULL,
        [AudienceMinimumOrders] int NOT NULL,
        [AudienceDays] int NULL,
        [Status] int NOT NULL,
        [ScheduledAt] datetimeoffset NOT NULL,
        [StartedAt] datetimeoffset NULL,
        [CompletedAt] datetimeoffset NULL,
        [CancelledAt] datetimeoffset NULL,
        [RecipientCount] int NOT NULL,
        [ExcludedCount] int NOT NULL,
        [LockToken] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_MessageCampaigns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MessageCampaigns_MessageTemplates_TemplateId] FOREIGN KEY ([TemplateId]) REFERENCES [MessageTemplates] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE TABLE [OutgoingMessages] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Purpose] int NOT NULL,
        [CampaignId] uniqueidentifier NULL,
        [AppointmentId] uniqueidentifier NULL,
        [Phone] varchar(20) NOT NULL,
        [RecipientName] nvarchar(150) NULL,
        [Body] nvarchar(2000) NOT NULL,
        [ProviderTemplateName] varchar(100) NULL,
        [LanguageCode] varchar(10) NOT NULL,
        [ParametersJson] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [Attempts] int NOT NULL,
        [NextAttemptAt] datetimeoffset NOT NULL,
        [LockedUntil] datetimeoffset NULL,
        [LockToken] uniqueidentifier NOT NULL,
        [SentAt] datetimeoffset NULL,
        [DeliveredAt] datetimeoffset NULL,
        [ReadAt] datetimeoffset NULL,
        [ProviderMessageId] varchar(200) NULL,
        [LastError] nvarchar(500) NULL,
        [DeduplicationKey] varchar(120) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_OutgoingMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OutgoingMessages_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_OutgoingMessages_MessageCampaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [MessageCampaigns] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE INDEX [IX_MessageCampaigns_Due] ON [MessageCampaigns] ([Status], [ScheduledAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE INDEX [IX_MessageCampaigns_TemplateId] ON [MessageCampaigns] ([TemplateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE INDEX [IX_MessageCampaigns_TenantId_CreatedAt] ON [MessageCampaigns] ([TenantId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE INDEX [IX_MessageTemplates_TenantId_Purpose_IsActive] ON [MessageTemplates] ([TenantId], [Purpose], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MessagingOptOuts_TenantId_Phone] ON [MessagingOptOuts] ([TenantId], [Phone]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE INDEX [IX_OutgoingMessages_AppointmentId] ON [OutgoingMessages] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE INDEX [IX_OutgoingMessages_CampaignId] ON [OutgoingMessages] ([CampaignId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_OutgoingMessages_Pending] ON [OutgoingMessages] ([NextAttemptAt], [CreatedAt]) WHERE [Status] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_OutgoingMessages_ProviderMessageId] ON [OutgoingMessages] ([ProviderMessageId]) WHERE [ProviderMessageId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE INDEX [IX_OutgoingMessages_TenantId_CampaignId_Status] ON [OutgoingMessages] ([TenantId], [CampaignId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OutgoingMessages_TenantId_DeduplicationKey] ON [OutgoingMessages] ([TenantId], [DeduplicationKey]) WHERE [DeduplicationKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    CREATE INDEX [IX_OutgoingMessages_TenantId_Purpose_CreatedAt] ON [OutgoingMessages] ([TenantId], [Purpose], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004131615_AddMessagingCampaignsAndReminders', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004133416_AddOrderChannel'
)
BEGIN
    ALTER TABLE [Orders] ADD [Channel] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004133416_AddOrderChannel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004133416_AddOrderChannel', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    ALTER TABLE [Products] ADD [SatProductCode] nvarchar(8) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    ALTER TABLE [Products] ADD [SatUnitCode] nvarchar(3) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    ALTER TABLE [Products] ADD [TaxObject] int NOT NULL DEFAULT 2;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderRefundLines]') AND [c].[name] = N'Quantity');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [OrderRefundLines] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [OrderRefundLines] ALTER COLUMN [Quantity] decimal(18,3) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderItems]') AND [c].[name] = N'ReturnedQuantity');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [OrderItems] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [OrderItems] ALTER COLUMN [ReturnedQuantity] decimal(18,3) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrderItems]') AND [c].[name] = N'Quantity');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [OrderItems] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [OrderItems] ALTER COLUMN [Quantity] decimal(18,3) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [SatProductCode] nvarchar(8) NOT NULL DEFAULT N'01010101';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [SatUnitCode] nvarchar(3) NOT NULL DEFAULT N'H87';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [TaxObject] int NOT NULL DEFAULT 2;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [Unit] nvarchar(20) NOT NULL DEFAULT N'Unit';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    EXEC(N'UPDATE [oi]
    SET [Unit] = [p].[Unit],
        [SatUnitCode] = CASE [p].[Unit]
            WHEN N''Pack'' THEN N''XPK''
            WHEN N''Box'' THEN N''XBX''
            WHEN N''Pair'' THEN N''PR''
            WHEN N''Kg'' THEN N''KGM''
            WHEN N''G'' THEN N''GRM''
            WHEN N''Lb'' THEN N''LBR''
            WHEN N''Oz'' THEN N''ONZ''
            WHEN N''Liter'' THEN N''LTR''
            WHEN N''Ml'' THEN N''MLT''
            WHEN N''Meter'' THEN N''MTR''
            WHEN N''Roll'' THEN N''XRO''
            WHEN N''Hour'' THEN N''HUR''
            WHEN N''Session'' THEN N''E48''
            WHEN N''Service'' THEN N''E48''
            ELSE N''H87''
        END
    FROM [OrderItems] AS [oi]
    INNER JOIN [Products] AS [p] ON [p].[Id] = [oi].[ProductId]
    WHERE [p].[Unit] IN (N''Unit'', N''Pack'', N''Box'', N''Pair'', N''Kg'', N''G'', N''Lb'', N''Oz'',
        N''Liter'', N''Ml'', N''Portion'', N''Slice'', N''Serving'', N''Meter'', N''Roll'',
        N''Hour'', N''Session'', N''Service'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004142352_AddDecimalQuantitiesAndCfdiData'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004142352_AddDecimalQuantitiesAndCfdiData', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Orders]') AND [c].[name] = N'TableNumber');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [Orders] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [Orders] ALTER COLUMN [TableNumber] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    ALTER TABLE [Orders] ADD [DiningTableId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    ALTER TABLE [Orders] ADD [IsTab] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    ALTER TABLE [Orders] ADD [OpenTableId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [Round] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE TABLE [DiningTables] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(30) NOT NULL,
        [Area] nvarchar(50) NULL,
        [Seats] int NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_DiningTables] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE TABLE [KitchenTickets] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Round] int NOT NULL,
        [Status] int NOT NULL,
        [SentAt] datetimeoffset NOT NULL,
        [SentBy] nvarchar(256) NULL,
        [StartedAt] datetimeoffset NULL,
        [ReadyAt] datetimeoffset NULL,
        [ServedAt] datetimeoffset NULL,
        CONSTRAINT [PK_KitchenTickets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_KitchenTickets_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE TABLE [OrderItemOptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderItemId] uniqueidentifier NOT NULL,
        [OptionId] uniqueidentifier NOT NULL,
        [GroupName] nvarchar(80) NOT NULL,
        [OptionName] nvarchar(80) NOT NULL,
        [PriceDelta] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_OrderItemOptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderItemOptions_OrderItems_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE TABLE [ProductOptionGroups] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [MinSelections] int NOT NULL,
        [MaxSelections] int NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_ProductOptionGroups] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductOptionGroups_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE TABLE [ProductOptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [GroupId] uniqueidentifier NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [PriceDelta] decimal(18,2) NOT NULL,
        [IsAvailable] bit NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_ProductOptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductOptions_ProductOptionGroups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [ProductOptionGroups] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE INDEX [IX_Orders_DiningTableId] ON [Orders] ([DiningTableId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE INDEX [IX_Orders_OpenTableId] ON [Orders] ([OpenTableId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_TenantId_OpenTableId_Open] ON [Orders] ([TenantId], [OpenTableId]) WHERE [OpenTableId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE INDEX [IX_DiningTables_TenantId_IsActive_SortOrder] ON [DiningTables] ([TenantId], [IsActive], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DiningTables_TenantId_Name] ON [DiningTables] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE UNIQUE INDEX [IX_KitchenTickets_OrderId_Round] ON [KitchenTickets] ([OrderId], [Round]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE INDEX [IX_KitchenTickets_TenantId_Status] ON [KitchenTickets] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE INDEX [IX_OrderItemOptions_OrderItemId] ON [OrderItemOptions] ([OrderItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE INDEX [IX_ProductOptionGroups_ProductId] ON [ProductOptionGroups] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE INDEX [IX_ProductOptionGroups_TenantId_ProductId] ON [ProductOptionGroups] ([TenantId], [ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE INDEX [IX_ProductOptions_GroupId] ON [ProductOptions] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    CREATE INDEX [IX_ProductOptions_TenantId_GroupId] ON [ProductOptions] ([TenantId], [GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_DiningTables_DiningTableId] FOREIGN KEY ([DiningTableId]) REFERENCES [DiningTables] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_DiningTables_OpenTableId] FOREIGN KEY ([OpenTableId]) REFERENCES [DiningTables] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004150717_AddProductOptionsAndOpenTabs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004150717_AddProductOptionsAndOpenTabs', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004153003_AddOnlineMenu'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [OnlineMenuMode] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004153003_AddOnlineMenu'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004153003_AddOnlineMenu', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD [BranchId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    ALTER TABLE [InventoryLocations] ADD [BranchId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    ALTER TABLE [CashSessions] ADD [BranchId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE TABLE [Branches] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Description] nvarchar(500) NULL,
        [AddressLine1] nvarchar(250) NULL,
        [AddressLine2] nvarchar(250) NULL,
        [City] nvarchar(120) NULL,
        [State] nvarchar(120) NULL,
        [Country] nvarchar(120) NULL,
        [PostalCode] nvarchar(20) NULL,
        [ContactName] nvarchar(120) NULL,
        [ContactPhone] nvarchar(30) NULL,
        [IsDefault] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Branches] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE TABLE [DailySalesSnapshots] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [BusinessDate] date NOT NULL,
        [BranchId] uniqueidentifier NULL,
        [OrderType] int NOT NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [ComputedAt] datetimeoffset NOT NULL,
        [OrdersCreated] int NOT NULL,
        [PaidOrders] int NOT NULL,
        [PaymentsCount] int NOT NULL,
        [PaymentsAmount] decimal(18,2) NOT NULL,
        [CashAmount] decimal(18,2) NOT NULL,
        [CardAmount] decimal(18,2) NOT NULL,
        [TransferAmount] decimal(18,2) NOT NULL,
        [WalletAmount] decimal(18,2) NOT NULL,
        [OtherAmount] decimal(18,2) NOT NULL,
        [RefundsCount] int NOT NULL,
        [RefundsAmount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_DailySalesSnapshots] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN

    EXEC(N'INSERT INTO [Branches] ([Id], [TenantId], [Name], [Code], [Description], [AddressLine1], [AddressLine2], [City], [State],
        [Country], [PostalCode], [ContactName], [ContactPhone], [IsDefault], [IsActive], [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy])
    SELECT l.[Id], l.[TenantId], l.[Name], l.[Code], l.[Description], l.[AddressLine1], l.[AddressLine2], l.[City], l.[State],
        l.[Country], l.[PostalCode], l.[ContactName], l.[ContactPhone], 0, l.[IsActive], l.[CreatedAt], l.[CreatedBy], l.[UpdatedAt], l.[UpdatedBy]
    FROM [InventoryLocations] l;

    WITH ranked AS (
        SELECT l.[Id], ROW_NUMBER() OVER (
            PARTITION BY l.[TenantId]
            ORDER BY CASE WHEN l.[IsDefault] = 1 AND l.[IsActive] = 1 THEN 0 WHEN l.[IsActive] = 1 THEN 1 ELSE 2 END, l.[CreatedAt], l.[Id]) AS rn
        FROM [InventoryLocations] l
    )
    UPDATE b SET [IsDefault] = 1, [IsActive] = 1
    FROM [Branches] b
    INNER JOIN ranked r ON r.[Id] = b.[Id]
    WHERE r.rn = 1;

    INSERT INTO [Branches] ([Id], [TenantId], [Name], [Code], [IsDefault], [IsActive], [CreatedAt], [CreatedBy])
    SELECT NEWID(), t.[Id], N''Sucursal principal'', N''PRINCIPAL'', 1, 1, SYSDATETIMEOFFSET(), N''migration''
    FROM [Tenants] t
    WHERE NOT EXISTS (SELECT 1 FROM [Branches] b WHERE b.[TenantId] = t.[Id]);

    UPDATE [InventoryLocations] SET [BranchId] = [Id];');

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN

    EXEC(N'UPDATE [CashSessions] SET [BranchId] = [InventoryLocationId] WHERE [InventoryLocationId] IS NOT NULL;

    UPDATE s SET [BranchId] = b.[Id]
    FROM [CashSessions] s
    INNER JOIN [Branches] b ON b.[TenantId] = s.[TenantId] AND b.[IsDefault] = 1
    WHERE s.[BranchId] IS NULL;');

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN

    EXEC(N'UPDATE o SET [BranchId] = p.[InventoryLocationId]
    FROM [Orders] o
    CROSS APPLY (
        SELECT TOP (1) op.[InventoryLocationId]
        FROM [OrderPayments] op
        WHERE op.[TenantId] = o.[TenantId] AND op.[OrderId] = o.[Id] AND op.[InventoryLocationId] IS NOT NULL
        ORDER BY op.[PaidAt], op.[Id]) p;

    UPDATE o SET [BranchId] = b.[Id]
    FROM [Orders] o
    INNER JOIN [Branches] b ON b.[TenantId] = o.[TenantId] AND b.[IsDefault] = 1
    WHERE o.[BranchId] IS NULL;');

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE INDEX [IX_Orders_BranchId] ON [Orders] ([BranchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE INDEX [IX_Orders_TenantId_BranchId_CreatedAt] ON [Orders] ([TenantId], [BranchId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE INDEX [IX_InventoryLocations_BranchId] ON [InventoryLocations] ([BranchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE INDEX [IX_InventoryLocations_TenantId_BranchId] ON [InventoryLocations] ([TenantId], [BranchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE INDEX [IX_CashSessions_BranchId] ON [CashSessions] ([BranchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE INDEX [IX_CashSessions_TenantId_BranchId_OpenedAt] ON [CashSessions] ([TenantId], [BranchId], [OpenedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Branches_TenantId_Code] ON [Branches] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Branches_TenantId_Default] ON [Branches] ([TenantId]) WHERE [IsDefault] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE INDEX [IX_Branches_TenantId_IsActive] ON [Branches] ([TenantId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DailySalesSnapshots_TenantId_BusinessDate_BranchId_OrderType] ON [DailySalesSnapshots] ([TenantId], [BusinessDate], [BranchId], [OrderType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    ALTER TABLE [CashSessions] ADD CONSTRAINT [FK_CashSessions_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    ALTER TABLE [InventoryLocations] ADD CONSTRAINT [FK_InventoryLocations_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004160303_AddBranchesAndDailySalesSnapshots'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004160303_AddBranchesAndDailySalesSnapshots', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE TABLE [Plans] (
        [Id] uniqueidentifier NOT NULL,
        [Code] nvarchar(30) NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [Description] nvarchar(500) NULL,
        [MonthlyPrice] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Modules] int NOT NULL,
        [MaxBranches] int NOT NULL,
        [MaxUsers] int NOT NULL,
        [TrialDays] int NOT NULL,
        [IsDefaultForNewBusinesses] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_Plans] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE TABLE [TenantSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [TrialEndsAt] datetimeoffset NOT NULL,
        [PaidThrough] datetimeoffset NULL,
        [SuspendedByPlatform] bit NOT NULL,
        [StatusReason] nvarchar(500) NULL,
        [CancelledAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_TenantSubscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantSubscriptions_Plans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [Plans] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE TABLE [SubscriptionPayments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SubscriptionId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Months] int NOT NULL,
        [Method] int NOT NULL,
        [Reference] nvarchar(120) NULL,
        [PaidAt] datetimeoffset NOT NULL,
        [PeriodStart] datetimeoffset NOT NULL,
        [PeriodEnd] datetimeoffset NOT NULL,
        [RecordedBy] nvarchar(256) NOT NULL,
        CONSTRAINT [PK_SubscriptionPayments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionPayments_TenantSubscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [TenantSubscriptions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Plans_Code] ON [Plans] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Plans_DefaultForNewBusinesses] ON [Plans] ([IsDefaultForNewBusinesses]) WHERE [IsDefaultForNewBusinesses] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPayments_PaidAt] ON [SubscriptionPayments] ([PaidAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPayments_SubscriptionId] ON [SubscriptionPayments] ([SubscriptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPayments_TenantId_PaidAt] ON [SubscriptionPayments] ([TenantId], [PaidAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptions_PlanId] ON [TenantSubscriptions] ([PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptions_Status] ON [TenantSubscriptions] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TenantSubscriptions_TenantId] ON [TenantSubscriptions] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004163845_AddPlansAndSubscriptions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004163845_AddPlansAndSubscriptions', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency'
)
BEGIN
    ALTER TABLE [Orders] ADD [CapturedOfflineAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency'
)
BEGIN
    ALTER TABLE [Orders] ADD [ClientRequestId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_TenantId_ClientRequestId] ON [Orders] ([TenantId], [ClientRequestId]) WHERE [ClientRequestId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004165450_AddOfflineSaleIdempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004165450_AddOfflineSaleIdempotency', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004203922_AddOrderNotificationPolicy'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [OrderNotificationPolicy] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004203922_AddOrderNotificationPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004203922_AddOrderNotificationPolicy', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004203947_AlignTenantAdminMemberships'
)
BEGIN

    UPDATE tu SET [Role] = 1
    FROM [TenantUsers] tu
    INNER JOIN [AspNetUsers] u ON u.[Id] = tu.[UserId]
    WHERE u.[Role] = 1
      AND tu.[Role] = 2
      AND NOT EXISTS (SELECT 1 FROM [TenantUsers] other WHERE other.[UserId] = tu.[UserId] AND other.[Id] <> tu.[Id]);

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004203947_AlignTenantAdminMemberships'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004203947_AlignTenantAdminMemberships', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004213919_UseMexicanPesosByDefault'
)
BEGIN
    UPDATE [TenantSettings] SET [Currency] = 'MXN' WHERE [Currency] = 'USD';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004213919_UseMexicanPesosByDefault'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004213919_UseMexicanPesosByDefault', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004221936_AddAppointmentResourcesAndStockPolicy'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [InsufficientStockPolicy] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004221936_AddAppointmentResourcesAndStockPolicy'
)
BEGIN
    ALTER TABLE [Appointments] ADD [ServiceProductId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004221936_AddAppointmentResourcesAndStockPolicy'
)
BEGIN
    ALTER TABLE [Appointments] ADD [StaffUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004221936_AddAppointmentResourcesAndStockPolicy'
)
BEGIN
    CREATE INDEX [IX_Appointments_TenantId_StaffUserId_StartAt] ON [Appointments] ([TenantId], [StaffUserId], [StartAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004221936_AddAppointmentResourcesAndStockPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004221936_AddAppointmentResourcesAndStockPolicy', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    ALTER TABLE [WhatsAppConversations] ADD [AutoRepliesPausedUntil] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    ALTER TABLE [Customers] ADD [Notes] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    CREATE TABLE [CustomerAddresses] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [Label] nvarchar(60) NOT NULL,
        [AddressLine] nvarchar(300) NOT NULL,
        [References] nvarchar(500) NULL,
        [IsDefault] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_CustomerAddresses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomerAddresses_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    CREATE INDEX [IX_CustomerAddresses_CustomerId] ON [CustomerAddresses] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    CREATE INDEX [IX_CustomerAddresses_TenantId_CustomerId] ON [CustomerAddresses] ([TenantId], [CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004224714_AddCustomerAddressesAndChatPause'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004224714_AddCustomerAddressesAndChatPause', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004232541_AddChargePermissionAndSmsCooldown'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [SmsCodeSentAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004232541_AddChargePermissionAndSmsCooldown'
)
BEGIN

    UPDATE tu SET [PermissionCodes] = tu.[PermissionCodes] + N',ChargeOrders'
    FROM [TenantUsers] tu
    INNER JOIN [AspNetUsers] u ON u.[Id] = tu.[UserId]
    WHERE tu.[PermissionCodes] IS NOT NULL
      AND (N',' + tu.[PermissionCodes] + N',') LIKE N'%,ManageOrders,%'
      AND (N',' + tu.[PermissionCodes] + N',') NOT LIKE N'%,ChargeOrders,%'
      AND u.[Role] <> 3;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004232541_AddChargePermissionAndSmsCooldown'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004232541_AddChargePermissionAndSmsCooldown', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003434_AddFolioSequences'
)
BEGIN
    CREATE TABLE [FolioSequences] (
        [TenantId] uniqueidentifier NOT NULL,
        [Scope] nvarchar(40) NOT NULL,
        [LastNumber] bigint NOT NULL,
        CONSTRAINT [PK_FolioSequences] PRIMARY KEY ([TenantId], [Scope]),
        CONSTRAINT [FK_FolioSequences_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003434_AddFolioSequences'
)
BEGIN
    INSERT INTO [FolioSequences] ([TenantId], [Scope], [LastNumber])
    SELECT t.[Id], N'ORD', ISNULL(MAX(TRY_CAST(SUBSTRING(o.[OrderNumber], 5, 18) AS bigint)), 0)
    FROM [Tenants] t
    LEFT JOIN [Orders] o ON o.[TenantId] = t.[Id] AND o.[OrderNumber] LIKE N'ORD-%'
    GROUP BY t.[Id];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003434_AddFolioSequences'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005003434_AddFolioSequences', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003444_AddPaymentTipAndCashReceived'
)
BEGIN
    ALTER TABLE [OrderPayments] ADD [CashReceived] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003444_AddPaymentTipAndCashReceived'
)
BEGIN
    ALTER TABLE [OrderPayments] ADD [Tip] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005003444_AddPaymentTipAndCashReceived'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005003444_AddPaymentTipAndCashReceived', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    ALTER TABLE [SupplyItems] ADD [ProductId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    CREATE INDEX [IX_SupplyItems_ProductId] ON [SupplyItems] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SupplyItems_TenantId_ProductId] ON [SupplyItems] ([TenantId], [ProductId]) WHERE [ProductId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    ALTER TABLE [SupplyItems] ADD CONSTRAINT [FK_SupplyItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005022701_AddProductStockItems'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005022701_AddProductStockItems', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005045624_AddPurchaseOrderClosing'
)
BEGIN
    ALTER TABLE [PurchaseOrders] ADD [CloseReason] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005045624_AddPurchaseOrderClosing'
)
BEGIN
    ALTER TABLE [PurchaseOrders] ADD [ClosedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005045624_AddPurchaseOrderClosing'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005045624_AddPurchaseOrderClosing', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RefreshTokens]') AND [c].[name] = N'Token');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [RefreshTokens] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [RefreshTokens] ALTER COLUMN [Token] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    ALTER TABLE [RefreshTokens] ADD [RevocationReason] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    ALTER TABLE [RefreshTokens] ADD [SessionId] uniqueidentifier NOT NULL DEFAULT (NEWID());
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    ALTER TABLE [RefreshTokens] ADD [TokenHash] varchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_SessionId] ON [RefreshTokens] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]) WHERE [TokenHash] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005092153_RevocableSessions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005092153_RevocableSessions', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    ALTER TABLE [OrderStatusHistory] ADD [TenantId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [TenantId] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    EXEC(N'UPDATE i SET i.[TenantId] = o.[TenantId]
    FROM [OrderItems] AS i
    INNER JOIN [Orders] AS o ON o.[Id] = i.[OrderId]
    WHERE i.[TenantId] = ''00000000-0000-0000-0000-000000000000''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    EXEC(N'UPDATE h SET h.[TenantId] = o.[TenantId]
    FROM [OrderStatusHistory] AS h
    INNER JOIN [Orders] AS o ON o.[Id] = h.[OrderId]
    WHERE h.[TenantId] = ''00000000-0000-0000-0000-000000000000''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    CREATE INDEX [IX_OrderStatusHistory_TenantId_OrderId] ON [OrderStatusHistory] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    CREATE INDEX [IX_OrderItems_TenantId_OrderId] ON [OrderItems] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005095544_ScopeOrderLinesAndHistoryByTenant'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005095544_ScopeOrderLinesAndHistoryByTenant', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005175643_StopUsingPlainRefreshTokenValues'
)
BEGIN
    EXEC(N'
    UPDATE [RefreshTokens]
    SET [TokenHash] = CONVERT(varchar(64), HASHBYTES(''SHA2_256'', CONVERT(varchar(500), [Token])), 2)
    WHERE [TokenHash] IS NULL AND [Token] <> N'''';

    UPDATE [RefreshTokens]
    SET [RevocationReason] = 1
    WHERE [RevokedAt] IS NOT NULL AND [RevocationReason] IS NULL AND [ReplacedByToken] IS NOT NULL;

    UPDATE [RefreshTokens]
    SET [Token] = N'''', [ReplacedByToken] = NULL
    WHERE [Token] <> N'''' OR [ReplacedByToken] IS NOT NULL;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005175643_StopUsingPlainRefreshTokenValues'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005175643_StopUsingPlainRefreshTokenValues', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns'
)
BEGIN
    DROP INDEX [IX_RefreshTokens_Token] ON [RefreshTokens];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns'
)
BEGIN
    DECLARE @var7 nvarchar(max);
    SELECT @var7 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RefreshTokens]') AND [c].[name] = N'ReplacedByToken');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [RefreshTokens] DROP CONSTRAINT ' + @var7 + ';');
    ALTER TABLE [RefreshTokens] DROP COLUMN [ReplacedByToken];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns'
)
BEGIN
    DECLARE @var8 nvarchar(max);
    SELECT @var8 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RefreshTokens]') AND [c].[name] = N'Token');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [RefreshTokens] DROP CONSTRAINT ' + @var8 + ';');
    ALTER TABLE [RefreshTokens] DROP COLUMN [Token];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006054141_DropPlainRefreshTokenColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006054141_DropPlainRefreshTokenColumns', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006101452_StopMappingStockAndLoyaltyColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006101452_StopMappingStockAndLoyaltyColumns', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    DECLARE @var9 nvarchar(max);
    SELECT @var9 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Products]') AND [c].[name] = N'LowStockThreshold');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT ' + @var9 + ';');
    ALTER TABLE [Products] DROP COLUMN [LowStockThreshold];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    DECLARE @var10 nvarchar(max);
    SELECT @var10 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Products]') AND [c].[name] = N'StockQuantity');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT ' + @var10 + ';');
    ALTER TABLE [Products] DROP COLUMN [StockQuantity];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    DECLARE @var11 nvarchar(max);
    SELECT @var11 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CustomerProfiles]') AND [c].[name] = N'LoyaltyPoints');
    IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [CustomerProfiles] DROP CONSTRAINT ' + @var11 + ';');
    ALTER TABLE [CustomerProfiles] DROP COLUMN [LoyaltyPoints];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    DECLARE @var12 nvarchar(max);
    SELECT @var12 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CustomerProfiles]') AND [c].[name] = N'LoyaltyTier');
    IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [CustomerProfiles] DROP CONSTRAINT ' + @var12 + ';');
    ALTER TABLE [CustomerProfiles] DROP COLUMN [LoyaltyTier];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006201255_DropStockAndLoyaltyColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006201255_DropStockAndLoyaltyColumns', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [CancelledAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [CancelledQuantity] decimal(18,3) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    CREATE TABLE [OrderItemCancellations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [OrderItemId] uniqueidentifier NOT NULL,
        [Round] int NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [ReturnedToStock] bit NOT NULL,
        [CancelledAt] datetimeoffset NOT NULL,
        [CancelledBy] nvarchar(256) NOT NULL,
        CONSTRAINT [PK_OrderItemCancellations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderItemCancellations_OrderItems_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems] ([Id]),
        CONSTRAINT [FK_OrderItemCancellations_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    CREATE INDEX [IX_OrderItemCancellations_OrderId] ON [OrderItemCancellations] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    CREATE INDEX [IX_OrderItemCancellations_OrderItemId] ON [OrderItemCancellations] ([OrderItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    CREATE INDEX [IX_OrderItemCancellations_TenantId_CancelledAt] ON [OrderItemCancellations] ([TenantId], [CancelledAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    CREATE INDEX [IX_OrderItemCancellations_TenantId_OrderId] ON [OrderItemCancellations] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008072542_AddTabItemCancellations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008072542_AddTabItemCancellations', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008074715_StopMappingRequireOpenCashSession'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008074715_StopMappingRequireOpenCashSession', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009041644_StopMappingRequireOpenCashSession'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009041644_StopMappingRequireOpenCashSession', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009043433_AddBusinessTheme'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [Theme] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009043433_AddBusinessTheme'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009043433_AddBusinessTheme', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009071957_DropRequireOpenCashSession'
)
BEGIN
    DECLARE @var13 nvarchar(max);
    SELECT @var13 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSettings]') AND [c].[name] = N'RequireOpenCashSession');
    IF @var13 IS NOT NULL EXEC(N'ALTER TABLE [TenantSettings] DROP CONSTRAINT ' + @var13 + ';');
    ALTER TABLE [TenantSettings] DROP COLUMN [RequireOpenCashSession];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009071957_DropRequireOpenCashSession'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009071957_DropRequireOpenCashSession', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010074254_AddOpeningHours'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [OpeningHours] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010074254_AddOpeningHours'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261010074254_AddOpeningHours', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011161835_AddSpecialDays'
)
BEGIN
    CREATE TABLE [SpecialDays] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [PeriodId] uniqueidentifier NOT NULL,
        [Date] date NOT NULL,
        [Kind] int NOT NULL,
        [FirstOpens] time(0) NULL,
        [FirstCloses] time(0) NULL,
        [SecondOpens] time(0) NULL,
        [SecondCloses] time(0) NULL,
        [Reason] nvarchar(40) NULL,
        [Revision] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_SpecialDays] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SpecialDays_Kind] CHECK ([Kind] IN (0, 1)),
        CONSTRAINT [CK_SpecialDays_Openings] CHECK (([Kind] = 0 AND [FirstOpens] IS NULL AND [FirstCloses] IS NULL AND [SecondOpens] IS NULL AND [SecondCloses] IS NULL) OR ([Kind] = 1 AND [FirstOpens] IS NOT NULL AND [FirstCloses] IS NOT NULL AND (([SecondOpens] IS NULL AND [SecondCloses] IS NULL) OR ([SecondOpens] IS NOT NULL AND [SecondCloses] IS NOT NULL)))),
        CONSTRAINT [CK_SpecialDays_Reason] CHECK ([Reason] IS NULL OR LEN([Reason]) > 0),
        CONSTRAINT [CK_SpecialDays_Revision] CHECK ([Revision] >= 1),
        CONSTRAINT [CK_SpecialDays_SecondOpening] CHECK ([SecondOpens] IS NULL OR ([FirstCloses] > [FirstOpens] AND [SecondOpens] >= [FirstCloses] AND [SecondCloses] <> [SecondOpens]))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011161835_AddSpecialDays'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SpecialDays_TenantId_Date] ON [SpecialDays] ([TenantId], [Date]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011161835_AddSpecialDays'
)
BEGIN
    CREATE INDEX [IX_SpecialDays_TenantId_PeriodId] ON [SpecialDays] ([TenantId], [PeriodId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261011161835_AddSpecialDays'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261011161835_AddSpecialDays', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    ALTER TABLE [DailySalesSnapshots] ADD [PlatformAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    CREATE TABLE [DeliveryPlatformSettings] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Channel] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CommissionPercent] decimal(5,2) NOT NULL,
        [CommissionBase] int NOT NULL,
        [CommissionVatPercent] decimal(5,2) NOT NULL,
        [IsrWithholdingPercent] decimal(5,2) NOT NULL,
        [VatWithholdingPercent] decimal(5,2) NOT NULL,
        [Revision] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_DeliveryPlatformSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DeliveryPlatformSettings_Channel] CHECK ([Channel] IN (3, 4, 5)),
        CONSTRAINT [CK_DeliveryPlatformSettings_CommissionBase] CHECK ([CommissionBase] IN (0, 1)),
        CONSTRAINT [CK_DeliveryPlatformSettings_Percents] CHECK ([CommissionPercent] BETWEEN 0 AND 100 AND [CommissionVatPercent] BETWEEN 0 AND 100 AND [IsrWithholdingPercent] BETWEEN 0 AND 100 AND [VatWithholdingPercent] BETWEEN 0 AND 100),
        CONSTRAINT [CK_DeliveryPlatformSettings_Revision] CHECK ([Revision] >= 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    CREATE TABLE [PlatformOrderDetails] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Channel] int NOT NULL,
        [ExternalOrderNumber] nvarchar(30) NOT NULL,
        [CollectedBy] int NOT NULL,
        [SettingsRevision] int NOT NULL,
        [CommissionPercent] decimal(5,2) NOT NULL,
        [CommissionBase] int NOT NULL,
        [CommissionVatPercent] decimal(5,2) NOT NULL,
        [IsrWithholdingPercent] decimal(5,2) NOT NULL,
        [VatWithholdingPercent] decimal(5,2) NOT NULL,
        [OrderTotal] decimal(18,2) NOT NULL,
        [Commission] decimal(18,2) NOT NULL,
        [CommissionTax] decimal(18,2) NOT NULL,
        [IsrWithheld] decimal(18,2) NOT NULL,
        [VatWithheld] decimal(18,2) NOT NULL,
        [ExpectedSettlement] decimal(18,2) NOT NULL,
        [RegisteredAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PlatformOrderDetails] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PlatformOrderDetails_Amounts] CHECK ([OrderTotal] > 0 AND [Commission] >= 0 AND [CommissionTax] >= 0 AND [IsrWithheld] >= 0 AND [VatWithheld] >= 0),
        CONSTRAINT [CK_PlatformOrderDetails_BusinessCollected] CHECK ([CollectedBy] = 0 OR ([IsrWithheld] = 0 AND [VatWithheld] = 0)),
        CONSTRAINT [CK_PlatformOrderDetails_Channel] CHECK ([Channel] IN (3, 4, 5)),
        CONSTRAINT [CK_PlatformOrderDetails_CollectedBy] CHECK ([CollectedBy] IN (0, 1)),
        CONSTRAINT [CK_PlatformOrderDetails_CommissionBase] CHECK ([CommissionBase] IN (0, 1)),
        CONSTRAINT [CK_PlatformOrderDetails_Number] CHECK (LEN([ExternalOrderNumber]) > 0),
        CONSTRAINT [CK_PlatformOrderDetails_Percents] CHECK ([CommissionPercent] BETWEEN 0 AND 100 AND [CommissionVatPercent] BETWEEN 0 AND 100 AND [IsrWithholdingPercent] BETWEEN 0 AND 100 AND [VatWithholdingPercent] BETWEEN 0 AND 100),
        CONSTRAINT [FK_PlatformOrderDetails_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeliveryPlatformSettings_TenantId_Channel] ON [DeliveryPlatformSettings] ([TenantId], [Channel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PlatformOrderDetails_OrderId] ON [PlatformOrderDetails] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    CREATE INDEX [IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderNumber_RegisteredAt] ON [PlatformOrderDetails] ([TenantId], [Channel], [ExternalOrderNumber], [RegisteredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    CREATE INDEX [IX_PlatformOrderDetails_TenantId_RegisteredAt] ON [PlatformOrderDetails] ([TenantId], [RegisteredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012005540_AddDeliveryPlatformOrders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012005540_AddDeliveryPlatformOrders', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    CREATE TABLE [ProductOptionPlatformPrices] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OptionId] uniqueidentifier NOT NULL,
        [Channel] int NOT NULL,
        [PriceDelta] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_ProductOptionPlatformPrices] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductOptionPlatformPrices_Channel] CHECK ([Channel] IN (3, 4, 5)),
        CONSTRAINT [CK_ProductOptionPlatformPrices_PriceDelta] CHECK ([PriceDelta] BETWEEN -100000 AND 100000),
        CONSTRAINT [FK_ProductOptionPlatformPrices_ProductOptions_OptionId] FOREIGN KEY ([OptionId]) REFERENCES [ProductOptions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    CREATE TABLE [ProductPlatformPrices] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [Channel] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_ProductPlatformPrices] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductPlatformPrices_Channel] CHECK ([Channel] IN (3, 4, 5)),
        CONSTRAINT [CK_ProductPlatformPrices_Price] CHECK ([Price] > 0),
        CONSTRAINT [FK_ProductPlatformPrices_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductOptionPlatformPrices_OptionId_Channel] ON [ProductOptionPlatformPrices] ([OptionId], [Channel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    CREATE INDEX [IX_ProductOptionPlatformPrices_TenantId_Channel] ON [ProductOptionPlatformPrices] ([TenantId], [Channel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductPlatformPrices_ProductId_Channel] ON [ProductPlatformPrices] ([ProductId], [Channel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    CREATE INDEX [IX_ProductPlatformPrices_TenantId_Channel] ON [ProductPlatformPrices] ([TenantId], [Channel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012112823_AddPlatformPrices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012112823_AddPlatformPrices', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    CREATE TABLE [PlatformSettlements] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Channel] int NOT NULL,
        [PeriodFrom] date NOT NULL,
        [PeriodTo] date NOT NULL,
        [DepositedOn] date NOT NULL,
        [DepositedAmount] decimal(18,2) NOT NULL,
        [Reference] nvarchar(60) NULL,
        [Notes] nvarchar(500) NULL,
        [Status] int NOT NULL,
        [OrdersTotal] decimal(18,2) NOT NULL,
        [AdjustmentsTotal] decimal(18,2) NOT NULL,
        [ClosedAt] datetimeoffset NULL,
        [VoidedAt] datetimeoffset NULL,
        [VoidReason] nvarchar(500) NULL,
        [Revision] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_PlatformSettlements] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PlatformSettlements_Channel] CHECK ([Channel] IN (3, 4, 5)),
        CONSTRAINT [CK_PlatformSettlements_Closed] CHECK ([Status] <> 1 OR [ClosedAt] IS NOT NULL),
        CONSTRAINT [CK_PlatformSettlements_Dates] CHECK ([PeriodTo] >= [PeriodFrom] AND [DepositedOn] >= [PeriodFrom]),
        CONSTRAINT [CK_PlatformSettlements_Revision] CHECK ([Revision] >= 1),
        CONSTRAINT [CK_PlatformSettlements_Status] CHECK ([Status] IN (0, 1, 99)),
        CONSTRAINT [CK_PlatformSettlements_Voided] CHECK ([Status] <> 99 OR ([VoidedAt] IS NOT NULL AND [VoidReason] IS NOT NULL))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    CREATE TABLE [PlatformSettlementAdjustments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SettlementId] uniqueidentifier NOT NULL,
        [Position] int NOT NULL,
        [Kind] int NOT NULL,
        [Description] nvarchar(200) NULL,
        [Amount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_PlatformSettlementAdjustments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PlatformSettlementAdjustments_Amount] CHECK ([Amount] <> 0),
        CONSTRAINT [CK_PlatformSettlementAdjustments_Kind] CHECK ([Kind] IN (1, 2, 3, 4, 5, 99)),
        CONSTRAINT [CK_PlatformSettlementAdjustments_Other] CHECK ([Kind] <> 99 OR [Description] IS NOT NULL),
        CONSTRAINT [FK_PlatformSettlementAdjustments_PlatformSettlements_SettlementId] FOREIGN KEY ([SettlementId]) REFERENCES [PlatformSettlements] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    CREATE TABLE [PlatformSettlementOrders] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SettlementId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [ExpectedNet] decimal(18,2) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_PlatformSettlementOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlatformSettlementOrders_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PlatformSettlementOrders_PlatformSettlements_SettlementId] FOREIGN KEY ([SettlementId]) REFERENCES [PlatformSettlements] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    CREATE INDEX [IX_PlatformSettlementAdjustments_SettlementId] ON [PlatformSettlementAdjustments] ([SettlementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PlatformSettlementOrders_OrderId_Active] ON [PlatformSettlementOrders] ([OrderId]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    CREATE INDEX [IX_PlatformSettlementOrders_SettlementId] ON [PlatformSettlementOrders] ([SettlementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    CREATE INDEX [IX_PlatformSettlementOrders_TenantId_OrderId] ON [PlatformSettlementOrders] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PlatformSettlements_TenantId_Channel_Open] ON [PlatformSettlements] ([TenantId], [Channel]) WHERE [Status] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    CREATE INDEX [IX_PlatformSettlements_TenantId_PeriodTo] ON [PlatformSettlements] ([TenantId], [PeriodTo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012145908_AddPlatformSettlements'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012145908_AddPlatformSettlements', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012204959_AddBranchServiceMode'
)
BEGIN
    ALTER TABLE [Branches] ADD [ServiceMode] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012204959_AddBranchServiceMode'
)
BEGIN
    EXEC(N'ALTER TABLE [Branches] ADD CONSTRAINT [CK_Branches_ServiceMode] CHECK ([ServiceMode] IN (0, 1))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012204959_AddBranchServiceMode'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012204959_AddBranchServiceMode', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013002425_AddDeliveryZones'
)
BEGIN
    CREATE TABLE [DeliveryZones] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(60) NOT NULL,
        [Fee] decimal(18,2) NOT NULL,
        [MinimumOrder] decimal(18,2) NOT NULL,
        [FreeDeliveryFrom] decimal(18,2) NULL,
        [IsActive] bit NOT NULL,
        [Revision] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_DeliveryZones] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DeliveryZones_Fee] CHECK ([Fee] >= 0 AND [Fee] <= 10000),
        CONSTRAINT [CK_DeliveryZones_FreeDeliveryFrom] CHECK ([FreeDeliveryFrom] IS NULL OR ([FreeDeliveryFrom] > 0 AND [FreeDeliveryFrom] <= 100000 AND [Fee] > 0)),
        CONSTRAINT [CK_DeliveryZones_MinimumOrder] CHECK ([MinimumOrder] >= 0 AND [MinimumOrder] <= 100000),
        CONSTRAINT [CK_DeliveryZones_Revision] CHECK ([Revision] >= 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013002425_AddDeliveryZones'
)
BEGIN
    CREATE TABLE [DeliveryZonePostalCodes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [DeliveryZoneId] uniqueidentifier NOT NULL,
        [PostalCode] varchar(5) NOT NULL,
        CONSTRAINT [PK_DeliveryZonePostalCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DeliveryZonePostalCodes_PostalCode] CHECK (LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'),
        CONSTRAINT [FK_DeliveryZonePostalCodes_DeliveryZones_DeliveryZoneId] FOREIGN KEY ([DeliveryZoneId]) REFERENCES [DeliveryZones] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013002425_AddDeliveryZones'
)
BEGIN
    CREATE INDEX [IX_DeliveryZonePostalCodes_DeliveryZoneId] ON [DeliveryZonePostalCodes] ([DeliveryZoneId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013002425_AddDeliveryZones'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeliveryZonePostalCodes_TenantId_PostalCode] ON [DeliveryZonePostalCodes] ([TenantId], [PostalCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013002425_AddDeliveryZones'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeliveryZones_TenantId_Name] ON [DeliveryZones] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013002425_AddDeliveryZones'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013002425_AddDeliveryZones', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013042014_AddOrderDeliveries'
)
BEGIN
    CREATE TABLE [OrderDeliveries] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [PostalCode] varchar(5) NOT NULL,
        [AddressLine] nvarchar(300) NOT NULL,
        [References] nvarchar(500) NULL,
        [ZoneName] nvarchar(60) NOT NULL,
        [Fee] decimal(18,2) NOT NULL,
        [FeeTaxRate] decimal(5,2) NOT NULL,
        [Courier] int NULL,
        [CourierFolio] varchar(30) NULL,
        [TrackingUrl] nvarchar(500) NULL,
        [ShipmentRegisteredAt] datetimeoffset NULL,
        [ShipmentRegisteredBy] nvarchar(256) NULL,
        CONSTRAINT [PK_OrderDeliveries] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrderDeliveries_AddressLine] CHECK (LEN([AddressLine]) > 0),
        CONSTRAINT [CK_OrderDeliveries_Fee] CHECK ([Fee] >= 0 AND [Fee] <= 10000),
        CONSTRAINT [CK_OrderDeliveries_FeeTaxRate] CHECK ([FeeTaxRate] >= 0 AND [FeeTaxRate] <= 100),
        CONSTRAINT [CK_OrderDeliveries_PostalCode] CHECK (LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'),
        CONSTRAINT [CK_OrderDeliveries_Shipment] CHECK (([Courier] IS NULL AND [CourierFolio] IS NULL AND [TrackingUrl] IS NULL AND [ShipmentRegisteredAt] IS NULL) OR ([Courier] IS NOT NULL AND [Courier] = 1 AND [CourierFolio] IS NOT NULL AND LEN([CourierFolio]) > 0 AND [ShipmentRegisteredAt] IS NOT NULL)),
        CONSTRAINT [CK_OrderDeliveries_TrackingUrl] CHECK ([TrackingUrl] IS NULL OR [TrackingUrl] LIKE 'https://_%'),
        CONSTRAINT [FK_OrderDeliveries_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013042014_AddOrderDeliveries'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrderDeliveries_OrderId] ON [OrderDeliveries] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013042014_AddOrderDeliveries'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013042014_AddOrderDeliveries', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    ALTER TABLE [OrderDeliveries] ADD [CashToCollect] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    ALTER TABLE [OrderDeliveries] ADD [DispatchedAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    ALTER TABLE [OrderDeliveries] ADD [DispatchedBy] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    ALTER TABLE [OrderDeliveries] ADD [PaysWith] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    CREATE TABLE [CourierSettlements] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Courier] int NOT NULL,
        [CashSessionId] uniqueidentifier NOT NULL,
        [Method] int NOT NULL,
        [HandedAmount] decimal(18,2) NOT NULL,
        [OrdersTotal] decimal(18,2) NOT NULL,
        [AdjustmentsTotal] decimal(18,2) NOT NULL,
        [Reference] nvarchar(60) NULL,
        [Notes] nvarchar(500) NULL,
        [SettledAt] datetimeoffset NOT NULL,
        [SettledBy] nvarchar(256) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_CourierSettlements] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CourierSettlements_Balances] CHECK ([HandedAmount] = [OrdersTotal] + [AdjustmentsTotal]),
        CONSTRAINT [CK_CourierSettlements_Courier] CHECK ([Courier] IN (1)),
        CONSTRAINT [CK_CourierSettlements_HandedAmount] CHECK ([HandedAmount] >= 0),
        CONSTRAINT [CK_CourierSettlements_Method] CHECK ([Method] IN (0, 2)),
        CONSTRAINT [FK_CourierSettlements_CashSessions_CashSessionId] FOREIGN KEY ([CashSessionId]) REFERENCES [CashSessions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    CREATE TABLE [CourierSettlementAdjustments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SettlementId] uniqueidentifier NOT NULL,
        [Position] int NOT NULL,
        [Kind] int NOT NULL,
        [Description] nvarchar(200) NULL,
        [Amount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_CourierSettlementAdjustments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CourierSettlementAdjustments_Amount] CHECK ([Amount] <> 0 AND ([Kind] = 99 OR [Amount] < 0)),
        CONSTRAINT [CK_CourierSettlementAdjustments_Kind] CHECK ([Kind] IN (1, 2, 3, 99)),
        CONSTRAINT [CK_CourierSettlementAdjustments_Other] CHECK ([Kind] <> 99 OR [Description] IS NOT NULL),
        CONSTRAINT [FK_CourierSettlementAdjustments_CourierSettlements_SettlementId] FOREIGN KEY ([SettlementId]) REFERENCES [CourierSettlements] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    CREATE TABLE [CourierSettlementOrders] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SettlementId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_CourierSettlementOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CourierSettlementOrders_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [FK_CourierSettlementOrders_CourierSettlements_SettlementId] FOREIGN KEY ([SettlementId]) REFERENCES [CourierSettlements] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CourierSettlementOrders_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    EXEC(N'ALTER TABLE [OrderDeliveries] ADD CONSTRAINT [CK_OrderDeliveries_CashToCollect] CHECK ([CashToCollect] IS NULL OR ([CashToCollect] > 0 AND [DispatchedAt] IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    EXEC(N'ALTER TABLE [OrderDeliveries] ADD CONSTRAINT [CK_OrderDeliveries_Dispatch] CHECK ([DispatchedAt] IS NULL OR [Courier] IS NOT NULL)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    EXEC(N'ALTER TABLE [OrderDeliveries] ADD CONSTRAINT [CK_OrderDeliveries_PaysWith] CHECK ([PaysWith] IS NULL OR ([PaysWith] > 0 AND [PaysWith] <= 100000))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    CREATE INDEX [IX_CourierSettlementAdjustments_SettlementId] ON [CourierSettlementAdjustments] ([SettlementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CourierSettlementOrders_OrderId] ON [CourierSettlementOrders] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    CREATE INDEX [IX_CourierSettlementOrders_SettlementId] ON [CourierSettlementOrders] ([SettlementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    CREATE INDEX [IX_CourierSettlementOrders_TenantId_OrderId] ON [CourierSettlementOrders] ([TenantId], [OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    CREATE INDEX [IX_CourierSettlements_CashSessionId] ON [CourierSettlements] ([CashSessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    CREATE INDEX [IX_CourierSettlements_TenantId_SettledAt] ON [CourierSettlements] ([TenantId], [SettledAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013085926_AddCourierSettlements'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013085926_AddCourierSettlements', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [HomeVisitTravelMinutes] int NOT NULL DEFAULT 30;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    ALTER TABLE [Appointments] ADD [SaleOrderId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    ALTER TABLE [Appointments] ADD [Version] rowversion NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    CREATE TABLE [AppointmentVisits] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [AppointmentId] uniqueidentifier NOT NULL,
        [PostalCode] varchar(5) NOT NULL,
        [AddressLine] nvarchar(300) NOT NULL,
        [References] nvarchar(500) NULL,
        [ZoneName] nvarchar(60) NOT NULL,
        [Fee] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_AppointmentVisits] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AppointmentVisits_AddressLine] CHECK (LEN([AddressLine]) > 0),
        CONSTRAINT [CK_AppointmentVisits_Fee] CHECK ([Fee] >= 0 AND [Fee] <= 10000),
        CONSTRAINT [CK_AppointmentVisits_PostalCode] CHECK (LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'),
        CONSTRAINT [FK_AppointmentVisits_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    CREATE TABLE [OrderVisitCharges] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [AppointmentId] uniqueidentifier NOT NULL,
        [ZoneName] nvarchar(60) NOT NULL,
        [Fee] decimal(18,2) NOT NULL,
        [FeeTaxRate] decimal(5,2) NOT NULL,
        CONSTRAINT [PK_OrderVisitCharges] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrderVisitCharges_Fee] CHECK ([Fee] > 0 AND [Fee] <= 10000),
        CONSTRAINT [CK_OrderVisitCharges_FeeTaxRate] CHECK ([FeeTaxRate] >= 0 AND [FeeTaxRate] <= 100),
        CONSTRAINT [FK_OrderVisitCharges_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OrderVisitCharges_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    EXEC(N'ALTER TABLE [TenantSettings] ADD CONSTRAINT [CK_TenantSettings_HomeVisitTravelMinutes] CHECK ([HomeVisitTravelMinutes] >= 0 AND [HomeVisitTravelMinutes] <= 240)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    CREATE INDEX [IX_Appointments_SaleOrderId] ON [Appointments] ([SaleOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppointmentVisits_AppointmentId] ON [AppointmentVisits] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    CREATE INDEX [IX_OrderVisitCharges_AppointmentId] ON [OrderVisitCharges] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrderVisitCharges_OrderId] ON [OrderVisitCharges] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    ALTER TABLE [Appointments] ADD CONSTRAINT [FK_Appointments_Orders_SaleOrderId] FOREIGN KEY ([SaleOrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013191311_AddHomeVisits'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013191311_AddHomeVisits', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [ListedInDirectory] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    ALTER TABLE [Tenants] ADD [DirectoryCategory] int NOT NULL DEFAULT 10;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    ALTER TABLE [Tenants] ADD [PublicDescription] nvarchar(160) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN

    EXEC(N'UPDATE [Tenants] SET [DirectoryCategory] = CASE
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI IN (N''Alimentos y bebidas'', N''Comida a domicilio'') THEN 0
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Abarrotes'' THEN 1
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI IN (N''Farmacia'', N''Consultorio'') THEN 2
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Ropa y accesorios'' THEN 3
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Electronica'' THEN 4
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Ferreteria'' THEN 5
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Papeleria'' THEN 6
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI IN (N''Estetica y spa'', N''Gimnasio'') THEN 7
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Taller mecanico'' THEN 8
        WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI IN (N''Servicios profesionales'', N''Educacion'', N''Lavanderia'') THEN 9
        WHEN [BusinessType] = 0 THEN 0
        WHEN [BusinessType] = 2 THEN 9
        ELSE 10
    END;');

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    EXEC(N'ALTER TABLE [Tenants] ADD CONSTRAINT [CK_Tenants_DirectoryCategory] CHECK ([DirectoryCategory] >= 0 AND [DirectoryCategory] <= 10)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    EXEC(N'ALTER TABLE [Tenants] ADD CONSTRAINT [CK_Tenants_PublicDescription] CHECK ([PublicDescription] IS NULL OR LEN([PublicDescription]) > 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016162347_AddBusinessDirectory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016162347_AddBusinessDirectory', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017070332_AddAccountAddresses'
)
BEGIN
    CREATE TABLE [AccountAddresses] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Label] nvarchar(60) NOT NULL,
        [PostalCode] varchar(5) NOT NULL,
        [AddressLine] nvarchar(300) NOT NULL,
        [References] nvarchar(500) NULL,
        [LastUsedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(max) NULL,
        CONSTRAINT [PK_AccountAddresses] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AccountAddresses_PostalCode] CHECK (LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'),
        CONSTRAINT [FK_AccountAddresses_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017070332_AddAccountAddresses'
)
BEGIN
    CREATE INDEX [IX_DeliveryZonePostalCodes_PostalCode] ON [DeliveryZonePostalCodes] ([PostalCode]) INCLUDE ([TenantId], [DeliveryZoneId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017070332_AddAccountAddresses'
)
BEGIN
    CREATE INDEX [IX_AccountAddresses_UserId_LastUsedAt] ON [AccountAddresses] ([UserId], [LastUsedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017070332_AddAccountAddresses'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017070332_AddAccountAddresses', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017100104_AddOrderTrackingToken'
)
BEGIN
    ALTER TABLE [Orders] ADD [TrackingToken] varchar(43) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017100104_AddOrderTrackingToken'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Orders_TrackingToken] ON [Orders] ([TrackingToken]) WHERE [TrackingToken] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017100104_AddOrderTrackingToken'
)
BEGIN
    EXEC(N'ALTER TABLE [Orders] ADD CONSTRAINT [CK_Orders_TrackingToken] CHECK ([TrackingToken] IS NULL OR LEN([TrackingToken]) = 43)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017100104_AddOrderTrackingToken'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017100104_AddOrderTrackingToken', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017183622_AddServiceOrderPhotos'
)
BEGIN
    CREATE TABLE [ServiceOrderPhotos] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [Stage] int NOT NULL,
        [FileUrl] nvarchar(500) NOT NULL,
        [FileName] nvarchar(255) NULL,
        [Note] nvarchar(300) NULL,
        [TakenAt] datetimeoffset NOT NULL,
        [TakenBy] nvarchar(256) NULL,
        [TakenByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ServiceOrderPhotos] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ServiceOrderPhotos_Stage] CHECK ([Stage] IN (0, 1)),
        CONSTRAINT [FK_ServiceOrderPhotos_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017183622_AddServiceOrderPhotos'
)
BEGIN
    CREATE INDEX [IX_ServiceOrderPhotos_OrderId] ON [ServiceOrderPhotos] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017183622_AddServiceOrderPhotos'
)
BEGIN
    CREATE INDEX [IX_ServiceOrderPhotos_TenantId_OrderId_TakenAt] ON [ServiceOrderPhotos] ([TenantId], [OrderId], [TakenAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017183622_AddServiceOrderPhotos'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017183622_AddServiceOrderPhotos', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [OnlineBookingCapacity] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    ALTER TABLE [TenantSettings] ADD [OnlineBookingEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    ALTER TABLE [Appointments] ADD [CancelledByCustomerAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    ALTER TABLE [Appointments] ADD [Channel] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    ALTER TABLE [Appointments] ADD [ClientRequestId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    ALTER TABLE [Appointments] ADD [CustomerUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    ALTER TABLE [Appointments] ADD [TrackingToken] varchar(43) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    EXEC(N'ALTER TABLE [TenantSettings] ADD CONSTRAINT [CK_TenantSettings_OnlineBookingCapacity] CHECK ([OnlineBookingCapacity] >= 1 AND [OnlineBookingCapacity] <= 20)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Appointments_CustomerUserId_StartAt] ON [Appointments] ([CustomerUserId], [StartAt]) WHERE [CustomerUserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Appointments_TenantId_ClientRequestId] ON [Appointments] ([TenantId], [ClientRequestId]) WHERE [ClientRequestId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Appointments_TrackingToken] ON [Appointments] ([TrackingToken]) WHERE [TrackingToken] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    EXEC(N'ALTER TABLE [Appointments] ADD CONSTRAINT [CK_Appointments_Channel] CHECK ([Channel] IN (0, 1))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    EXEC(N'ALTER TABLE [Appointments] ADD CONSTRAINT [CK_Appointments_TrackingToken] CHECK ([TrackingToken] IS NULL OR LEN([TrackingToken]) = 43)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    ALTER TABLE [Appointments] ADD CONSTRAINT [FK_Appointments_AspNetUsers_CustomerUserId] FOREIGN KEY ([CustomerUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017225015_AddOnlineBooking'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017225015_AddOnlineBooking', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018032200_AddBusinessCover'
)
BEGIN
    ALTER TABLE [Tenants] ADD [CoverImageUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261018032200_AddBusinessCover'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261018032200_AddBusinessCover', N'10.0.12');
END;

COMMIT;
GO

