# Finanzas, pagos e impuestos

## Estado actual

- Importes, costos, impuestos y existencias usan `decimal`; no se encontraron `float`/`double` para dinero en Domain/Application.
- `Order` calcula `SubTotal`, `Tax`, `Total`, `PaidAmount` y `Balance`. Admite pagos parciales y rechaza sobrepago.
- `OrderPayment` conserva método, fecha, ubicación opcional, evidencia URL, referencia y notas. La fecha del pago la pone el servidor (`PaidAt` ya no se acepta del cliente) y el importe admite máximo 2 decimales.
- Una orden cancelada no acepta pagos y una orden con pagos no puede cancelarse hasta que existan reembolsos (ver ADR 0007); `OrderDto.CanCancel` refleja ambas reglas.
- La evidencia de un pago debe ser un archivo subido por el endpoint de evidencias para el mismo tenant (`OrderUploadRules`); las imágenes se re-codifican y los PDF se sirven como descarga.
- `CashSession` conserva fondo, apertura/cierre, entradas, salidas y saldo esperado. `CashTransaction` puede referir orden, método y ubicación.
  - El signo del movimiento lo define su tipo (gasto, retiro y reembolso restan; depósito y venta suman); el importe capturado siempre es positivo con 2 decimales y la fecha es la del servidor.
  - Los movimientos manuales solo pueden ser depósito, gasto, retiro o reembolso; la orden referida debe existir.
  - `Inflows`/`Outflows` y el saldo esperado solo cuentan efectivo; tarjeta, transferencia y otros métodos se reportan aparte (`NonCashInflows`). El corte diario expone `NetCashOnlyFlow`.
  - Solo puede haber una caja abierta por tenant (índice único filtrado) y `CashSession.Revision` es token de concurrencia: un pago concurrente con el cierre o dos cierres simultáneos fallan con 409 en lugar de sobrescribirse. El cierre exige un monto contado mayor o igual a cero.
- Al registrar un pago, `OrderCashService` lo agrega a la sesión abierta más reciente si existe; si no hay sesión, el pago sigue registrado sin movimiento de caja.
- Compras aceptan `TaxPercent` por línea y calculan subtotales/impuesto.
- `DigitalWallet` existe como método de pago y subtotal del corte, no como cuenta Wallet.

## Impuesto actual y deuda

`Order.RecalculateTotals()` aplica `Math.Round(SubTotal * 0.16m, 2)` y suma el resultado. Es cálculo fijo de 16%, sin configuración por concepto, inclusión de impuesto, descuentos ni desglose persistido. No es una solución fiscal definitiva.

El carrito del SPA muestra subtotal, IVA y total estimados con la misma regla (centavos enteros y redondeo bancario) para que el cajero no cobre el subtotal como total; el total definitivo es el que devuelve la API. Sigue pendiente decidir si los precios del catálogo ya incluyen IVA: si lo incluyen, la regla actual sobrecobra.

El reporte operativo calcula la utilidad bruta sin IVA (`NetRevenue` − costo de ventas), toma como costo de ventas los consumos de las mismas órdenes reconocidas como ingreso y, sin rango explícito, usa el mes en la zona horaria del tenant.

Compras redondean línea a 4 decimales y órdenes a 2, sin política transversal documentada. La diferencia debe resolverse antes de ampliar contabilidad. No hay CFDI, SAT ni notas de crédito fiscales implementados.

## Reglas aprobadas

- Nunca usar `float`/`double` para dinero.
- Backend realiza cálculos autoritativos.
- Moneda, precisión, midpoint rounding, nivel de redondeo y orden de operaciones son explícitos.
- No modificar/eliminar transacciones históricas para corregir saldo.
- Correcciones futuras usan ajustes crédito/débito trazables, relacionados con orden, pago o movimiento original.
- Los ajustes contemplan cuentas por cobrar, saldo a favor, Wallet y conciliación sin afirmar que esos módulos existan hoy.
- Evidencias de pago se tratan como archivo sensible tenant-scoped.
- Pago, orden, caja y ubicación deben compartir tenant y correlación verificable.

## Arquitectura objetivo: Tax Calculation Engine México

Un componente backend versionado deberá recibir contexto de entidad legal, fecha y líneas, y devolver un desglose persistible que soporte:

- precios con impuesto incluido o separado;
- múltiples tasas y conceptos exentos;
- reglas por artículo/servicio y entidad legal;
- orden explícito de descuentos;
- reglas uniformes de redondeo;
- desglose fiscal persistido con la transacción;
- preparación de datos para CFDI futuro.

La adopción requiere ADR/especificación fiscal, pruebas con casos límite, migración compatible y revisión contable/legal. El motor no se implementa en esta tarea.
