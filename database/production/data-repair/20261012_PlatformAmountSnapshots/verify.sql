-- =====================================================================================
-- TienditApp (FoodParkDigital): días del resumen diario que la versión anterior a la Fase 22F
-- guardó con lo que cobraron las plataformas en [OtherAmount] (un swap que cruzó la medianoche).
-- Solo lee. Ver README.md.
-- DesplegadoEn es la hora (UTC) desde la que la versión nueva ya corría en todas las instancias: solo
-- los días calculados antes de esa hora pudo guardarlos la versión anterior.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -v DesplegadoEn="2026-10-12T06:18:00Z" -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET QUOTED_IDENTIFIER ON;
GO

-- La selección de la consulta 7 del verify.sql de 20261012005540_AddDeliveryPlatformOrders, acotada
-- a lo que pudo guardar la versión anterior: días de negocios con pagos «Plataforma» (método 5) con
-- [PlatformAmount] 0, [OtherAmount] mayor que 0, calculados después del primer pago «Plataforma» del
-- negocio y antes de DesplegadoEn. [FilasDelDia] son las filas que repair.sql borra de ese día (todas
-- sus sucursales y tipos de pedido).
IF COL_LENGTH(N'dbo.DailySalesSnapshots', N'PlatformAmount') IS NULL
    SELECT N'Falta aplicar 20261012005540_AddDeliveryPlatformOrders: no hay nada que revisar.' AS [Aviso];
ELSE
    EXEC(N'WITH [Afectados] AS (
    SELECT DISTINCT s.[TenantId], s.[BusinessDate]
    FROM [DailySalesSnapshots] s
    INNER JOIN (
        SELECT [TenantId], MIN([PaidAt]) AS [PrimerPagoPlataforma]
        FROM [OrderPayments]
        WHERE [Method] = 5
        GROUP BY [TenantId]
    ) f ON f.[TenantId] = s.[TenantId]
    WHERE s.[PlatformAmount] = 0 AND s.[OtherAmount] > 0 AND s.[ComputedAt] > f.[PrimerPagoPlataforma]
        AND s.[ComputedAt] < CAST(N''$(DesplegadoEn)'' AS datetimeoffset)
)
SELECT a.[TenantId] AS [Negocio], a.[BusinessDate] AS [Dia], COUNT(*) AS [FilasDelDia],
    SUM(s.[OtherAmount]) AS [Otros], SUM(s.[PaymentsAmount]) AS [Cobrado], MAX(s.[ComputedAt]) AS [Calculado]
FROM [Afectados] a
INNER JOIN [DailySalesSnapshots] s ON s.[TenantId] = a.[TenantId] AND s.[BusinessDate] = a.[BusinessDate]
GROUP BY a.[TenantId], a.[BusinessDate]
ORDER BY a.[TenantId], a.[BusinessDate];');
