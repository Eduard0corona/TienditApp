-- =====================================================================================
-- TienditApp (FoodParkDigital): renombra las categorías repetidas de un negocio para que
-- 20261110022936_AddUniqueCategoryNamePerTenant pueda crear su índice único. En cada grupo de
-- categorías de un negocio con el mismo nombre según la intercalación de [Categories].[Name], la
-- activa (y entre iguales la más antigua) conserva su nombre; las demás reciben « (2)», « (3)»... con
-- el primer número libre en su negocio, recortando el nombre si no cabe en 150 caracteres. No borra
-- ni mueve nada: los productos se quedan en su categoría. Lista cada cambio: guarda la salida en el
-- registro del cambio. Córrelo antes de apply.sql, con un respaldo y después de avisar a los
-- negocios (ver README.md).
-- Idempotente: otra ejecución no encuentra repetidas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -W -s "|" -f o:65001 -i repair.sql -o repair-salida.txt
-- =====================================================================================
SET NOCOUNT ON;
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

DECLARE @Renamed TABLE (
    [TenantId] uniqueidentifier NOT NULL,
    [Id] uniqueidentifier NOT NULL,
    [Before] nvarchar(150) NOT NULL,
    [After] nvarchar(150) NOT NULL);

-- The lock holds until COMMIT: no category saved meanwhile repeats a name or takes a new one.
DECLARE @Pending TABLE (
    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
    [TenantId] uniqueidentifier NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [Place] int NOT NULL);
INSERT INTO @Pending ([Id], [TenantId], [Name], [Place])
SELECT r.[Id], r.[TenantId], r.[Name], r.[Place]
FROM (
    SELECT c.[Id], c.[TenantId], c.[Name],
        ROW_NUMBER() OVER (PARTITION BY c.[TenantId], c.[Name] ORDER BY c.[IsActive] DESC, c.[CreatedAt], c.[Id]) AS [Place]
    FROM [Categories] c WITH (TABLOCKX, HOLDLOCK)
) r
WHERE r.[Place] > 1;

DECLARE @Id uniqueidentifier, @TenantId uniqueidentifier, @Name nvarchar(150), @Number int,
    @Suffix nvarchar(12), @Candidate nvarchar(150);
DECLARE pending CURSOR LOCAL FAST_FORWARD FOR
    SELECT [Id], [TenantId], [Name] FROM @Pending ORDER BY [TenantId], [Name], [Place];
OPEN pending;
FETCH NEXT FROM pending INTO @Id, @TenantId, @Name;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Number = 2;
    WHILE 1 = 1
    BEGIN
        SET @Suffix = N' (' + CAST(@Number AS nvarchar(10)) + N')';
        SET @Candidate = LEFT(RTRIM(@Name), 150 - LEN(@Suffix)) + @Suffix;
        IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [TenantId] = @TenantId AND [Name] = @Candidate)
            BREAK;
        SET @Number += 1;
    END;

    UPDATE [Categories]
    SET [Name] = @Candidate, [UpdatedAt] = SYSDATETIMEOFFSET(), [UpdatedBy] = N'data-repair 20261110_RepeatedCategoryNames'
    WHERE [Id] = @Id;
    INSERT INTO @Renamed ([TenantId], [Id], [Before], [After]) VALUES (@TenantId, @Id, @Name, @Candidate);

    FETCH NEXT FROM pending INTO @Id, @TenantId, @Name;
END;
CLOSE pending;
DEALLOCATE pending;

COMMIT;

SELECT t.[Slug] AS [Negocio], r.[Id], r.[Before] AS [Antes], r.[After] AS [Ahora]
FROM @Renamed r
LEFT JOIN [Tenants] t ON t.[Id] = r.[TenantId]
ORDER BY t.[Slug], r.[After];
GO
