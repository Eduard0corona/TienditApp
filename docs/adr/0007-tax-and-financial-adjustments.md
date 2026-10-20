# ADR 0007: Impuestos y ajustes financieros

- Estado: Aceptada como dirección objetivo
- Fecha: 2026-08-10

## Contexto

Órdenes aplican 16% fijo; compras aceptan tasa por línea. No hay desglose fiscal, CFDI ni ledger de ajustes. Alterar transacciones históricas destruiría trazabilidad.

## Decisión

Mantener `decimal` y cálculos autoritativos en backend. Diseñar un Tax Calculation Engine México con impuesto incluido/separado, tasas múltiples, exentos, reglas por concepto/entidad legal, descuentos, redondeo explícito y desglose persistido preparado para CFDI futuro.

Correcciones futuras serán ajustes crédito/débito inmutables y relacionados con la operación original, con impacto explícito en CxC, saldo a favor, Wallet y conciliación.

## Consecuencias

El cálculo fijo actual queda como deuda, no solución fiscal. Implementar requiere definición contable/legal, modelo y migración compatible, pruebas y rollout; no se afirma que CFDI, SAT, notas de crédito o módulos de ajuste existan hoy.

## Actualización 2026-10-04 (Fase 1)

Primera etapa implementada: política de IVA por negocio (incluido o sumado), tasa por producto (16, 8, 0 % o exento), tasa guardada por línea, política guardada por orden y desglose calculado en el servidor con redondeo bancario por tasa (`OrderTaxCalculator`). Los negocios existentes conservan el IVA sumado al 16 %, que da los mismos totales que el cálculo anterior. Siguen pendientes descuentos, otros impuestos, datos CFDI, reembolsos y ajustes.

## Actualización 2026-10-04 (Fase 2)

Reembolsos implementados como compensación, no como edición: cada `OrderRefund` es inmutable, guarda importe, forma de pago, motivo, autor, fecha del servidor y las unidades devueltas; la orden acumula `RefundedAmount` y las líneas `ReturnedQuantity`, pero sus pagos y líneas no cambian. El efectivo devuelto es un movimiento de salida en la caja del cajero y los insumos devueltos son una entrada de inventario con referencia propia. Anular una venta es reembolsar el resto de cada pago y, si el estado lo permite, cancelarla. Siguen pendientes las notas de crédito fiscales, el saldo a favor, las cuentas por cobrar y la conciliación.

## Actualización 2026-10-19 (Fase 26A)

Primera parte de las cuentas por cobrar: el fiado ([ADR 0033](0033-customer-credit.md)). El dueño le pone a un cliente un límite de fiado; Vender vende fiado dentro de ese límite, revisado en el servidor con un candado por cliente; Clientes › Por cobrar dice quién debe; y un abono a la cuenta se reparte del pedido más viejo al más nuevo como pagos de cada pedido en la caja de quien cobra. No hay un libro de cuentas aparte: lo que se debe es el saldo de los pedidos entregados o vendidos fiado, y abonar es pagar, así que la caja, el corte y los reportes no cambian. Siguen pendientes condonar, el saldo a favor, intereses, plazos, recordatorios, Wallet y conciliación (Fase 26B en adelante).

## Actualización 2026-10-20 (Fase 26B)

Primer ajuste de crédito trazable: condonar ([ADR 0034](0034-customer-write-offs-and-credit-term.md)). El dueño o un administrador perdona todo o parte de lo que un cliente debe. Cada parte es un `OrderWriteOff` inmutable con monto, motivo, nota, fecha y autor, y baja el saldo del pedido (`Order.WrittenOffAmount`), sin tocar la venta, sus pagos ni la caja. El negocio también puede poner un plazo al fiado para ver lo vencido. Siguen pendientes las notas de crédito fiscales (lo condonado de una venta facturada), el saldo a favor, intereses, recordatorios, Wallet y conciliación.
