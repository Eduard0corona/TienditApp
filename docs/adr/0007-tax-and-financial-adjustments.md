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
