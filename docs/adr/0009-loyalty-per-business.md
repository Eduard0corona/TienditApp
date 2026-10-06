# ADR 0009: Lealtad por negocio, fuera de alcance hasta tener reglas

- Estado: Aceptada
- Fecha: 2026-10-05

## Contexto

El diagnóstico encontró que "los campos de lealtad existen sin usarse" y propone que Clientes sea "un registro por negocio con consentimiento, historial y lealtad".

Los campos son `CustomerProfile.LoyaltyPoints` y `LoyaltyTier`. `CustomerProfile` es el perfil de la cuenta de un cliente registrado y lo comparten todos los negocios donde compra. Nada los lee ni los escribe: el único método que los cambiaba (`SetLoyalty`) no tenía llamadas, no hay API ni pantalla y valen 0 y nulo en todas las filas.

El resto de esa propuesta ya existe por negocio:

- **Registro:** `Customer`, con notas y direcciones.
- **Historial:** la ficha del cliente, con pedidos y chat.
- **Consentimiento:** bajas de mensajería, por negocio.

Falta la lealtad, y no hay reglas de negocio para ella: cómo se ganan los puntos, cuánto valen, cuándo vencen, qué pasa con un reembolso, si hay niveles y cómo se factura un canje.

## Decisión

1. **La lealtad es de cada negocio.** Los puntos ganados en un negocio no valen en otro ni se ven ahí. Cuando se implemente, vivirá en un registro con `TenantId` (`ITenantScoped`), ligado al cliente de ese negocio, nunca en `CustomerProfile`.
2. **Es un libro de movimientos, no un saldo editable.** Ganar, canjear, vencer, revertir por reembolso y ajustar quedan como movimientos inmutables con autor, fecha del servidor y la operación que los originó. El saldo se calcula de ellos y una corrección compensa, igual que reembolsos, caja e inventario ([ADR 0007](0007-tax-and-financial-adjustments.md)). El backend decide cuántos puntos da una venta y cuánto descuenta un canje, con `decimal`.
3. **Queda fuera de alcance hasta que el negocio defina sus reglas:**
   - acumulación: por importe, por visita o por producto;
   - valor del punto y vigencia;
   - qué pasa con un reembolso o una anulación;
   - niveles;
   - cómo se refleja un canje en el ticket y en el CFDI (descuento o forma de pago);
   - los términos que el cliente acepta.
   Sin esas reglas no se agrega API ni pantalla.
4. **Los campos de `CustomerProfile` no se usan para esto.** Se quitó `SetLoyalty` para que nadie los escriba. Las columnas se quedan, porque borrarlas pide una migración con su `apply.sql` y no aporta nada mientras valgan 0. Se borran cuando se implemente la lealtad por negocio o en la próxima limpieza de columnas sin uso, como `Product.StockQuantity`.

## Consecuencias

- La deuda "puntos de lealtad sin usarse" pasa a ser una decisión explícita: no hay lealtad, y lo que existe no se usa por diseño.
- Implementarla requiere:
  - las reglas anteriores;
  - el agregado por negocio con su libro de movimientos;
  - eventos de venta, reembolso y anulación que muevan puntos (idempotentes, por el outbox);
  - una migración compatible y pruebas de aislamiento entre negocios.
- Ningún documento ni pantalla debe presentar la lealtad como existente.

## Seguimiento

- 2026-10-06 (Fase 12): el modelo dejó de mapear `LoyaltyPoints` y `LoyaltyTier` (`StopMappingStockAndLoyaltyColumns`, sin cambio de esquema); las columnas se borran en la siguiente fase. La decisión no cambia.
