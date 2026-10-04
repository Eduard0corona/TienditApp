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
