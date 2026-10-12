-- =====================================================================================
-- TienditApp (FoodParkDigital): borra los días del resumen diario que la versión anterior a la
-- Fase 22F guardó con lo que cobraron las plataformas en [OtherAmount], para que el worker los
-- vuelva a calcular. Córrelo solo después de revisar verify.sql y con la versión nueva desplegada
-- en todas las instancias; ver README.md.
-- DesplegadoEn es la hora (UTC) desde la que la versión nueva ya corría en todas las instancias: solo
-- los días calculados antes de esa hora pudo guardarlos la versión anterior. Sin ella, sqlcmd se
-- detiene sin borrar nada.
-- Solo borra filas de [DailySalesSnapshots] (datos derivados), todas las de cada día afectado.
-- Lista cada fila borrada: guarda la salida en el registro del cambio.
-- Idempotente: lo que vuelve a calcular el worker lleva una hora posterior a DesplegadoEn, así que
-- otra ejecución, ahora o después, no borra nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -b -W -s "|" -f o:65001 -v DesplegadoEn="2026-10-12T06:18:00Z" -i repair.sql -o repair-salida.txt
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

IF COL_LENGTH(N'dbo.DailySalesSnapshots', N'PlatformAmount') IS NULL
    THROW 50000, N'Apply 20261012005540_AddDeliveryPlatformOrders first: there is nothing to repair before it.', 1;
GO

BEGIN TRANSACTION;

-- La misma selección que verify.sql.
EXEC(N'DELETE s
OUTPUT deleted.[TenantId] AS [Negocio], deleted.[BusinessDate] AS [Dia], deleted.[BranchId] AS [Sucursal],
    deleted.[OrderType] AS [TipoDePedido], deleted.[PaymentsAmount] AS [Cobrado], deleted.[OtherAmount] AS [Otros],
    deleted.[ComputedAt] AS [Calculado]
FROM [DailySalesSnapshots] s
INNER JOIN (
    SELECT DISTINCT d.[TenantId], d.[BusinessDate]
    FROM [DailySalesSnapshots] d
    INNER JOIN (
        SELECT [TenantId], MIN([PaidAt]) AS [PrimerPagoPlataforma]
        FROM [OrderPayments]
        WHERE [Method] = 5
        GROUP BY [TenantId]
    ) f ON f.[TenantId] = d.[TenantId]
    WHERE d.[PlatformAmount] = 0 AND d.[OtherAmount] > 0 AND d.[ComputedAt] > f.[PrimerPagoPlataforma]
        AND d.[ComputedAt] < CAST(N''$(DesplegadoEn)'' AS datetimeoffset)
) a ON a.[TenantId] = s.[TenantId] AND a.[BusinessDate] = s.[BusinessDate];');

COMMIT;
GO
