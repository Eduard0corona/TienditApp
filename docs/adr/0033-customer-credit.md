# ADR 0033: Fiado (cuentas por cobrar de clientes)

- Estado: Aceptada
- Fecha: 2026-10-19

## Contexto

Los negocios de barrio fían: la tiendita, la fonda, la estética. En TienditApp ya se fiaba de hecho, sin decirlo:

- Vender guardaba una venta sin cobrar, con su saldo;
- Pedidos registraba abonos pedido por pedido;
- la ficha del cliente sumaba en «Debe» el saldo de sus pedidos no cancelados, también los que seguían en curso.

Faltaba lo que hace del fiado una cuenta:

- decir a quién se le fía y hasta cuánto;
- que el servidor revise ese límite al vender;
- ver en un lugar quién debe y cuánto;
- recibir un abono a la cuenta, no pedido por pedido;
- mostrarle al cliente su estado de cuenta.

Es la primera parte de «Ajustes financieros», la única fila «Objetivo no implementado» de [current-vs-target](../roadmap/current-vs-target.md), anticipada por el [ADR 0007](0007-tax-and-financial-adjustments.md). Las decisiones de la Fase 26A las tomé con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **A quién se le fía** (`Customer.CreditLimit`).
   - A un cliente del negocio, activo, con teléfono y que no es «Público en general» (`Customer.SellsOnCredit`). Un cliente desactivado conserva su límite, pero no se le fía.
   - El dueño o un administrador (`RequiresTenantManager`) le pone un límite de $1 a $1,000,000, a centavos, o se lo quita.
   - Cada cambio queda en la actividad del negocio (`CustomerCreditLimitUpdated`).
   - Bajar el límite por debajo de lo que debe se permite: solo impide fiarle más.
2. **Qué se debe** (`Receivables`, una sola regla para Por cobrar, el estado de cuenta, el límite y «Debe» en la ficha). Un pedido se debe cuando cumple todo esto:
   - no está cancelado y lleva el teléfono del cliente;
   - tiene saldo (total menos lo pagado);
   - se vendió fiado o ya se entregó (entregado o terminado).

   No se deben:
   - un pedido en curso: todavía no se entrega, así que no se debe;
   - uno que el repartidor salió a cobrar en efectivo ([ADR 0019](0019-courier-cash-on-delivery.md)): eso lo debe el repartidor, no el cliente.

   Los pedidos se juntan por el teléfono, como en la ficha: +52 y +521 son la misma persona. La ficha cambia: «Debe» ya no cuenta los pedidos en curso.
3. **Fiar en Vender** (`CreateOrderCommand.OnCredit`, que pide `ChargeOrders`).
   - Es solo una venta de mostrador del negocio: ni una cuenta de mesa, ni un pedido a domicilio, ni uno de plataforma. Se registra con conexión.
   - Va a un cliente al que se le fía, y la venta tiene que dejar saldo; el cliente puede dejar una parte.
   - Lo que debe más lo que deja la venta no pasa de su límite. Si pasa, el mensaje dice cuánto cobrar para que quepa.
   - El servidor lo revisa dentro del candado del cliente (`ICustomerCreditLock`: `sp_getapplock` por negocio y teléfono, de la transacción de la venta), tomado antes que el del folio. Así, dos ventas fiadas o una venta y un abono al mismo tiempo se cuentan entre sí.
   - El pedido queda `SoldOnCredit` y se entrega como una venta pagada: la comida con cocina, cuando está lista. Sigue fiado aunque luego se pague.
4. **Abonar a la cuenta** (`PayCustomerAccountCommand`, que pide `ChargeOrders`, con el mismo candado).
   - Un monto con un método que entra a caja (efectivo con cambio, tarjeta, transferencia, billetera u otro), repartido del pedido más viejo al más nuevo.
   - Cada parte es un pago de su pedido en la caja de quien cobra (`OrderPaymentRegistrar`), con la nota «Abono a la cuenta». El cambio va con la última parte.
   - Los pedidos que quedan pagados se cierran como una venta pagada.
   - Nunca más de lo que debe: todavía no hay saldo a favor.
   - Queda en la actividad del negocio (`CustomerAccountPaid`) con el reparto por folio, sin el teléfono.
5. **El estado de cuenta** (`GetCustomerAccountQuery`), por cliente o teléfono:
   - el límite, lo que debe y lo que le queda;
   - los pedidos que debe (hasta 100, del más viejo);
   - sus últimos 10 pagos.

   El cliente arma el resumen para «Enviar por WhatsApp» (wa.me): el servidor no manda nada.
6. **Clientes › Por cobrar** (`GetReceivablesQuery`), con `ManageCustomers` o `ChargeOrders`:
   - una fila por teléfono, con el nombre del cliente (o el de su último pedido), lo que debe, cuántos pedidos y desde cuándo;
   - su límite y si lo rebasa;
   - el total por cobrar;
   - búsqueda por nombre o parte del teléfono, y orden por lo que debe o por lo más viejo.
7. **Pedidos** marca «Fiado» en los pedidos `SoldOnCredit`.
8. **No cambian**:
   - el corte: un abono es un pago;
   - los reportes por cobros;
   - el CFDI: un pedido con saldo ya se timbra PPD;
   - los mensajes automáticos de WhatsApp.
9. **La migración** `20261019202512_AddCustomerCredit` agrega:
   - `Customers.CreditLimit` (`decimal(18,2)`, con nulos);
   - `Orders.SoldOnCredit` (`bit`, 0 por omisión);
   - el índice `IX_Orders_TenantId_CustomerPhone`.

## Consecuencias

- **Lo que logra:**
  - El negocio decide a quién le fía y hasta cuánto. El servidor lo cumple aunque dos cajas vendan al mismo cliente a la vez.
  - Lo que se fió deja de estar repartido en pedidos sueltos: hay una lista de quién debe y una cuenta por cliente que se abona de una vez.
  - La caja y los reportes no cambian: fiar no es cobrar y abonar sí lo es.
- **Lo que limita** (queda para la 26B):
  - Sin condonar una deuda ni devolver lo fiado sin dinero. Un pedido entregado no se cancela y un reembolso necesita algo pagado.
  - Sin saldo a favor: ni anticipos ni abonos de más.
  - Sin intereses, plazos ni recordatorios automáticos.
  - Lo fiado va por teléfono: un cliente que cambia de número empieza otra cuenta, y una venta al cliente con otro teléfono se rechaza.
  - Sin conexión no se fía: el límite se revisa al vender.
  - Un pedido entregado sin pagar cuenta como deuda aunque no se haya vendido fiado, también contra el límite.
- **Reversibilidad:**
  - Volver a la versión anterior funciona sin tocar la base: no conoce las columnas, y los saldos fiados se siguen viendo como saldos que se cobran desde el pedido.
  - `rollback.sql` borra las columnas y el índice solo si ningún cliente tiene límite y ninguna venta se cerró fiada. En cuanto un negocio fía, se detiene con el error 50026 sin cambiar nada, y solo queda corregir hacia adelante.
