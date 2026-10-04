-- =====================================================================================
-- TienditApp (FoodParkDigital): movimientos manuales de inventario guardados con la
-- numeración desfasada de la pantalla de inventario. Solo lectura: no modifica nada.
--
-- Hasta la corrección, el cliente numeraba tipos y motivos desde 0 y la API y la base desde 1,
-- así que cada movimiento registrado en la pantalla se guardó con el tipo y el motivo
-- SIGUIENTES a los elegidos: "Salida" quedó como Entry y sumó existencias, "Ajuste (+)"
-- como Exit y restó. Este script lista esos movimientos con lo que se eligió en pantalla,
-- lo que aplicó el sistema y la corrección pendiente. El procedimiento está en README.md.
--
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
--
-- Las consultas 2 a 4 muestran datos capturados por el negocio (usuarios, notas,
-- contrapartes): trátalos como datos del cliente y no los copies a tickets ni a PRs.
-- =====================================================================================
SET NOCOUNT ON;

-- Tenant a revisar. Con NULL solo corre el resumen por tenant (consulta 1).
DECLARE @TenantId UNIQUEIDENTIFIER = NULL;

-- Fecha y hora UTC del despliegue de la corrección: los movimientos manuales registrados
-- después ya usan la numeración correcta. NULL = ahora (antes de desplegarla).
DECLARE @CorreccionDesplegadaUtc DATETIMEOFFSET = NULL;

DECLARE @Limite DATETIMEOFFSET = COALESCE(@CorreccionDesplegadaUtc, SYSDATETIMEOFFSET());

-- Clasificación de cada movimiento ([Origen]), igual en las cuatro consultas:
--   Sistema       recepciones de compra (PO:...:RECV:...), consumos de órdenes y piezas
--                 (ORDER:...:CONSUMPTION) y sus reversos (ORDER:...:REVERSAL), con el tipo y
--                 el motivo que les asigna el código; nunca pasaron por la pantalla.
--   Compensacion  referencia externa CORR:<folio>:R o CORR:<folio>:I (README.md, paso 4).
--   Posterior     manual registrado después de @CorreccionDesplegadaUtc.
--   Sospechoso    manual anterior con un tipo (1-7) y un motivo (1-10) que la pantalla
--                 desfasada pudo enviar.
--   NoExplicable  manual anterior con un valor que esa pantalla no enviaba (tipo 8 o
--                 motivo 99): vino de otro cliente de la API y este procedimiento no aplica.

-- 1. Resumen por tenant: cuántos movimientos sospechosos hay y cuántos ya tienen reverso y
--    recaptura. Sale vacío si ningún tenant registró movimientos manuales antes del límite.
WITH Movimientos AS (
    SELECT m.TenantId, m.MovementNumber, m.CreatedAt,
        CASE
            WHEN m.ExternalReference LIKE N'ORDER:%:CONSUMPTION' AND m.MovementType = 2 AND m.MovementReason IN (2, 4) THEN N'Sistema'
            WHEN m.ExternalReference LIKE N'ORDER:%:REVERSAL' AND m.MovementType = 1 AND m.MovementReason = 9 THEN N'Sistema'
            WHEN m.ExternalReference LIKE N'PO:%:RECV:%' AND m.MovementType = 1 AND m.MovementReason = 1 THEN N'Sistema'
            WHEN m.ExternalReference LIKE N'CORR:%' THEN N'Compensacion'
            WHEN m.CreatedAt >= @Limite THEN N'Posterior'
            WHEN m.MovementType BETWEEN 1 AND 7 AND m.MovementReason BETWEEN 1 AND 10 THEN N'Sospechoso'
            ELSE N'NoExplicable'
        END AS Origen
    FROM dbo.InventoryMovements m
)
SELECT
    m.TenantId AS [TenantId],
    t.[Name] AS [Tenant],
    SUM(CASE WHEN m.Origen = N'Sospechoso' THEN 1 ELSE 0 END) AS [Sospechosos],
    SUM(CASE WHEN m.Origen = N'Sospechoso' AND rev.Id IS NOT NULL AND rec.Id IS NOT NULL THEN 1 ELSE 0 END) AS [Compensados],
    SUM(CASE WHEN m.Origen = N'NoExplicable' THEN 1 ELSE 0 END) AS [NoExplicables],
    MIN(m.CreatedAt) AS [PrimeroUtc],
    MAX(m.CreatedAt) AS [UltimoUtc]
FROM Movimientos m
LEFT JOIN dbo.Tenants t ON t.Id = m.TenantId
LEFT JOIN dbo.InventoryMovements rev
    ON rev.TenantId = m.TenantId AND rev.ExternalReference = CONCAT(N'CORR:', m.MovementNumber, N':R')
LEFT JOIN dbo.InventoryMovements rec
    ON rec.TenantId = m.TenantId AND rec.ExternalReference = CONCAT(N'CORR:', m.MovementNumber, N':I')
WHERE m.Origen IN (N'Sospechoso', N'NoExplicable')
GROUP BY m.TenantId, t.[Name]
ORDER BY [Sospechosos] DESC, t.[Name];

IF @TenantId IS NULL
BEGIN
    PRINT N'Asigna @TenantId para ver el detalle del tenant (consultas 2 a 4).';
END
ELSE
BEGIN
    -- 2. Detalle por línea. [TipoElegido]/[MotivoElegido]: lo que el usuario eligió en la lista
    --    (el valor guardado + 1; para el motivo, 10 + 1 = 99 "Otro"). [TipoGuardado]/
    --    [MotivoGuardado]: lo que aplicó el sistema y lo que la pantalla corregida muestra.
    --    [CorreccionNeta]: diferencia entre el efecto elegido y el aplicado (siempre el doble
    --    de la cantidad, con el signo contrario al aplicado). [Reverso]/[Recaptura]: folios de
    --    las compensaciones ya registradas. [CostoPromedio]: si la línea pudo mover el costo
    --    promedio del insumo; desde el 2026-10-01 solo lo mueven las entradas (Entry) con costo,
    --    antes cualquier línea que sumaba existencias con costo.
    WITH Movimientos AS (
        SELECT m.Id, m.TenantId, m.MovementNumber, m.MovementType, m.MovementReason, m.MovementDate,
            m.CreatedAt, m.CreatedBy, m.ReferenceNumber, m.ExternalReference, m.DocumentNumber,
            m.CounterpartyName, m.Notes,
            CASE
                WHEN m.ExternalReference LIKE N'ORDER:%:CONSUMPTION' AND m.MovementType = 2 AND m.MovementReason IN (2, 4) THEN N'Sistema'
                WHEN m.ExternalReference LIKE N'ORDER:%:REVERSAL' AND m.MovementType = 1 AND m.MovementReason = 9 THEN N'Sistema'
                WHEN m.ExternalReference LIKE N'PO:%:RECV:%' AND m.MovementType = 1 AND m.MovementReason = 1 THEN N'Sistema'
                WHEN m.ExternalReference LIKE N'CORR:%' THEN N'Compensacion'
                WHEN m.CreatedAt >= @Limite THEN N'Posterior'
                WHEN m.MovementType BETWEEN 1 AND 7 AND m.MovementReason BETWEEN 1 AND 10 THEN N'Sospechoso'
                ELSE N'NoExplicable'
            END AS Origen
        FROM dbo.InventoryMovements m
        WHERE m.TenantId = @TenantId
    ),
    Tipos AS (
        SELECT Valor, Etiqueta, Signo
        FROM (VALUES
            (1, N'Entrada', 1),
            (2, N'Salida', -1),
            (3, N'Ajuste (+)', 1),
            (4, N'Ajuste (-)', -1),
            (5, N'Transferencia entrada', 1),
            (6, N'Transferencia salida', -1),
            (7, N'Devolucion produccion', 1),
            (8, N'Devolucion proveedor', -1)
        ) AS v (Valor, Etiqueta, Signo)
    ),
    Motivos AS (
        SELECT Valor, Etiqueta
        FROM (VALUES
            (1, N'Compra'), (2, N'Venta'), (3, N'Produccion'), (4, N'Consumo'), (5, N'Merma'),
            (6, N'Danado'), (7, N'Conteo fisico'), (8, N'Carga inicial'), (9, N'Devolucion'),
            (10, N'Ajuste manual'), (99, N'Otro')
        ) AS v (Valor, Etiqueta)
    )
    SELECT
        m.MovementNumber AS [Folio],
        m.Origen AS [Clasificacion],
        m.MovementDate AS [FechaMovimiento],
        m.CreatedAt AS [RegistradoUtc],
        m.CreatedBy AS [RegistradoPor],
        CASE WHEN elegido.Valor IS NOT NULL THEN CONCAT(elegido.Etiqueta, N' (', elegido.Valor, N')') END AS [TipoElegido],
        CASE WHEN motivoElegido.Valor IS NOT NULL THEN CONCAT(motivoElegido.Etiqueta, N' (', motivoElegido.Valor, N')') END AS [MotivoElegido],
        CONCAT(COALESCE(guardado.Etiqueta, N'?'), N' (', m.MovementType, N')') AS [TipoGuardado],
        CONCAT(COALESCE(motivoGuardado.Etiqueta, N'?'), N' (', m.MovementReason, N')') AS [MotivoGuardado],
        s.[Name] AS [Insumo],
        loc.[Name] AS [Ubicacion],
        l.Quantity AS [Cantidad],
        l.UnitCost AS [CostoUnitario],
        l.QuantityBefore AS [ExistenciaAntes],
        l.QuantityAfter AS [ExistenciaDespues],
        guardado.Signo * l.Quantity AS [EfectoAplicado],
        elegido.Signo * l.Quantity AS [EfectoElegido],
        (elegido.Signo - guardado.Signo) * l.Quantity AS [CorreccionNeta],
        CASE
            WHEN l.UnitCost IS NULL OR guardado.Signo IS NULL OR guardado.Signo < 0 THEN N'No'
            WHEN m.MovementType = 1 THEN N'Sí'
            ELSE N'Solo si se registró antes del 2026-10-01'
        END AS [CostoPromedio],
        rev.MovementNumber AS [Reverso],
        rec.MovementNumber AS [Recaptura],
        m.ReferenceNumber AS [Referencia],
        m.ExternalReference AS [ReferenciaExterna],
        m.DocumentNumber AS [Documento],
        m.CounterpartyName AS [Contraparte],
        m.Notes AS [Notas],
        l.Notes AS [NotasLinea],
        m.Id AS [MovimientoId],
        l.SupplyItemId AS [InsumoId],
        l.InventoryLocationId AS [UbicacionId]
    FROM Movimientos m
    JOIN dbo.InventoryMovementLines l ON l.MovementId = m.Id AND l.TenantId = m.TenantId
    JOIN dbo.SupplyItems s ON s.Id = l.SupplyItemId AND s.TenantId = m.TenantId
    JOIN dbo.InventoryLocations loc ON loc.Id = l.InventoryLocationId AND loc.TenantId = m.TenantId
    LEFT JOIN Tipos guardado ON guardado.Valor = m.MovementType
    LEFT JOIN Motivos motivoGuardado ON motivoGuardado.Valor = m.MovementReason
    LEFT JOIN Tipos elegido
        ON m.Origen = N'Sospechoso' AND elegido.Valor = m.MovementType + 1
    LEFT JOIN Motivos motivoElegido
        ON m.Origen = N'Sospechoso'
        AND motivoElegido.Valor = CASE WHEN m.MovementReason = 10 THEN 99 ELSE m.MovementReason + 1 END
    LEFT JOIN dbo.InventoryMovements rev
        ON rev.TenantId = m.TenantId AND rev.ExternalReference = CONCAT(N'CORR:', m.MovementNumber, N':R')
    LEFT JOIN dbo.InventoryMovements rec
        ON rec.TenantId = m.TenantId AND rec.ExternalReference = CONCAT(N'CORR:', m.MovementNumber, N':I')
    WHERE m.Origen IN (N'Sospechoso', N'NoExplicable')
    ORDER BY m.CreatedAt, m.MovementNumber, s.[Name], loc.[Name];

    -- 3. Por insumo y ubicación con sospechosos o compensaciones. [ExistenciaSegunKardex] suma
    --    todas las líneas con el signo de su tipo guardado y debe coincidir con
    --    [ExistenciaActual] ([DiferenciaKardex] = 0); si no, hay otro descuadre que revisar antes.
    --    [CorreccionPendiente] es lo que falta registrar de cada sospechoso: el reverso si no
    --    existe CORR:<folio>:R y la recaptura si no existe CORR:<folio>:I. Supone que el negocio
    --    confirmó todos; una [ExistenciaProyectada] negativa indica que lo elegido no pudo ser
    --    la intención real. Tras un conteo físico (README.md, opción B) manda el conteo.
    WITH Movimientos AS (
        SELECT m.Id, m.TenantId, m.MovementNumber, m.MovementType,
            CASE
                WHEN m.ExternalReference LIKE N'ORDER:%:CONSUMPTION' AND m.MovementType = 2 AND m.MovementReason IN (2, 4) THEN N'Sistema'
                WHEN m.ExternalReference LIKE N'ORDER:%:REVERSAL' AND m.MovementType = 1 AND m.MovementReason = 9 THEN N'Sistema'
                WHEN m.ExternalReference LIKE N'PO:%:RECV:%' AND m.MovementType = 1 AND m.MovementReason = 1 THEN N'Sistema'
                WHEN m.ExternalReference LIKE N'CORR:%' THEN N'Compensacion'
                WHEN m.CreatedAt >= @Limite THEN N'Posterior'
                WHEN m.MovementType BETWEEN 1 AND 7 AND m.MovementReason BETWEEN 1 AND 10 THEN N'Sospechoso'
                ELSE N'NoExplicable'
            END AS Origen
        FROM dbo.InventoryMovements m
        WHERE m.TenantId = @TenantId
    ),
    Lineas AS (
        SELECT
            l.SupplyItemId,
            l.InventoryLocationId,
            -- Mismo signo que aplica el código; un tipo fuera del enum suma (ResolveDelta).
            CASE WHEN m.MovementType IN (2, 4, 6, 8) THEN -l.Quantity ELSE l.Quantity END AS Efecto,
            CASE WHEN m.Origen = N'Sospechoso' THEN 1 ELSE 0 END AS EsSospechoso,
            CASE WHEN m.Origen = N'Sospechoso' THEN
                    CASE WHEN rev.Id IS NULL THEN CASE WHEN m.MovementType IN (2, 4, 6) THEN l.Quantity ELSE -l.Quantity END ELSE 0 END
                  + CASE WHEN rec.Id IS NULL THEN CASE WHEN m.MovementType IN (2, 4, 6) THEN l.Quantity ELSE -l.Quantity END ELSE 0 END
                ELSE 0
            END AS Pendiente,
            CASE WHEN m.Origen = N'Compensacion' THEN 1 ELSE 0 END AS EsCompensacion
        FROM Movimientos m
        JOIN dbo.InventoryMovementLines l ON l.MovementId = m.Id AND l.TenantId = m.TenantId
        LEFT JOIN dbo.InventoryMovements rev
            ON m.Origen = N'Sospechoso' AND rev.TenantId = m.TenantId
            AND rev.ExternalReference = CONCAT(N'CORR:', m.MovementNumber, N':R')
        LEFT JOIN dbo.InventoryMovements rec
            ON m.Origen = N'Sospechoso' AND rec.TenantId = m.TenantId
            AND rec.ExternalReference = CONCAT(N'CORR:', m.MovementNumber, N':I')
    )
    SELECT
        s.[Name] AS [Insumo],
        loc.[Name] AS [Ubicacion],
        COALESCE(st.OnHandQuantity, 0) AS [ExistenciaActual],
        SUM(li.Efecto) AS [ExistenciaSegunKardex],
        COALESCE(st.OnHandQuantity, 0) - SUM(li.Efecto) AS [DiferenciaKardex],
        SUM(li.EsSospechoso) AS [LineasSospechosas],
        SUM(li.EsCompensacion) AS [LineasCompensacion],
        SUM(li.Pendiente) AS [CorreccionPendiente],
        COALESCE(st.OnHandQuantity, 0) + SUM(li.Pendiente) AS [ExistenciaProyectada],
        li.SupplyItemId AS [InsumoId],
        li.InventoryLocationId AS [UbicacionId]
    FROM Lineas li
    JOIN dbo.SupplyItems s ON s.Id = li.SupplyItemId AND s.TenantId = @TenantId
    JOIN dbo.InventoryLocations loc ON loc.Id = li.InventoryLocationId AND loc.TenantId = @TenantId
    LEFT JOIN dbo.InventoryStocks st
        ON st.TenantId = @TenantId AND st.SupplyItemId = li.SupplyItemId AND st.InventoryLocationId = li.InventoryLocationId
    GROUP BY s.[Name], loc.[Name], st.OnHandQuantity, li.SupplyItemId, li.InventoryLocationId
    HAVING SUM(li.EsSospechoso) > 0 OR SUM(li.EsCompensacion) > 0
    ORDER BY s.[Name], loc.[Name];

    -- 4. Insumos cuyo costo promedio pudo alterarse: tienen líneas sospechosas que sumaron
    --    existencias con costo (ver [CostoPromedio] en la consulta 2). Las compensaciones no lo
    --    corrigen; README.md, "Costo promedio", explica cómo revisarlo.
    WITH Movimientos AS (
        SELECT m.Id, m.TenantId, m.MovementType, m.CreatedAt,
            CASE
                WHEN m.ExternalReference LIKE N'ORDER:%:CONSUMPTION' AND m.MovementType = 2 AND m.MovementReason IN (2, 4) THEN N'Sistema'
                WHEN m.ExternalReference LIKE N'ORDER:%:REVERSAL' AND m.MovementType = 1 AND m.MovementReason = 9 THEN N'Sistema'
                WHEN m.ExternalReference LIKE N'PO:%:RECV:%' AND m.MovementType = 1 AND m.MovementReason = 1 THEN N'Sistema'
                WHEN m.ExternalReference LIKE N'CORR:%' THEN N'Compensacion'
                WHEN m.CreatedAt >= @Limite THEN N'Posterior'
                WHEN m.MovementType BETWEEN 1 AND 7 AND m.MovementReason BETWEEN 1 AND 10 THEN N'Sospechoso'
                ELSE N'NoExplicable'
            END AS Origen
        FROM dbo.InventoryMovements m
        WHERE m.TenantId = @TenantId
    )
    SELECT
        s.[Name] AS [Insumo],
        s.AverageCost AS [CostoPromedioActual],
        s.LastCost AS [UltimoCosto],
        s.DefaultCost AS [CostoPredeterminado],
        SUM(CASE WHEN m.MovementType = 1 THEN 1 ELSE 0 END) AS [LineasQueLoMovieron],
        SUM(CASE WHEN m.MovementType <> 1 THEN 1 ELSE 0 END) AS [LineasQuePudieronMoverlo],
        SUM(l.Quantity) AS [CantidadSumada],
        MIN(m.CreatedAt) AS [PrimeraUtc],
        MAX(m.CreatedAt) AS [UltimaUtc],
        s.Id AS [InsumoId]
    FROM Movimientos m
    JOIN dbo.InventoryMovementLines l ON l.MovementId = m.Id AND l.TenantId = m.TenantId
    JOIN dbo.SupplyItems s ON s.Id = l.SupplyItemId AND s.TenantId = m.TenantId
    WHERE m.Origen = N'Sospechoso'
      AND m.MovementType IN (1, 3, 5, 7)
      AND l.UnitCost IS NOT NULL
    GROUP BY s.[Name], s.AverageCost, s.LastCost, s.DefaultCost, s.Id
    ORDER BY s.[Name];
END
