# Finanzas, pagos e impuestos

## Estado actual

- Importes, costos, impuestos y existencias usan `decimal`; no se encontraron `float`/`double` para dinero en Domain/Application.
- `Order` calcula `SubTotal`, `Tax`, `Total`, `PaidAmount` y `Balance`. Admite pagos parciales y rechaza sobrepago.
- `OrderPayment` conserva método, fecha, ubicación opcional, evidencia URL, referencia y notas.
- `CashSession` conserva fondo, apertura/cierre, entradas, salidas y saldo esperado. `CashTransaction` puede referir orden, método y ubicación.
- Al registrar un pago, `OrderCashService` lo agrega a la sesión abierta más reciente si existe; si no hay sesión, el pago sigue registrado sin movimiento de caja.
- Compras aceptan `TaxPercent` por línea y calculan subtotales/impuesto.
- `DigitalWallet` existe como método de pago y subtotal del corte, no como cuenta Wallet.

## Impuesto actual y deuda

`Order.RecalculateTotals()` aplica `Math.Round(SubTotal * 0.16m, 2)` y suma el resultado. Es cálculo fijo de 16%, sin configuración por concepto, inclusión de impuesto, descuentos ni desglose persistido. No es una solución fiscal definitiva.

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
