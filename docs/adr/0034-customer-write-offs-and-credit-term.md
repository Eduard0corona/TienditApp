# ADR 0034: Condonar y plazo del fiado

- Estado: Aceptada
- Fecha: 2026-10-20

## Contexto

La Fase 26A ([ADR 0033](0033-customer-credit.md)) hizo del fiado una cuenta: a quién se le fía y hasta cuánto, quién debe y un abono a la cuenta. Dejó pendientes dos cosas que un negocio de barrio hace todos los días:

- **Perdonar lo que un cliente debe.** El cliente se mudó, se cobró algo de más o el dueño decide regalarlo. Sin eso, la deuda quedaba para siempre en Por cobrar: un pedido entregado no se cancela, y un reembolso necesita algo pagado.
- **Saber qué ya venció.** «Debe $1,160» no dice si es de ayer o de hace tres semanas, y el negocio cobra primero lo más atrasado.

El [ADR 0007](0007-tax-and-financial-adjustments.md) pide que toda corrección financiera compense el historial con un registro trazable, sin borrarlo. También quedó abierta una pregunta de la 26A: entregar sin cobrar, ¿pide `ChargeOrders` o revisa el límite?

Tomé las decisiones de la Fase 26B con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Condonar es un registro aparte** (`OrderWriteOff`, inmutable, como un reembolso).
   - Guarda el pedido, el monto, el motivo, la nota, la fecha y quién lo hizo.
   - Los motivos (`OrderWriteOffReason`): no se va a cobrar, error al registrar, cortesía u otro. «Otro» exige una nota de 3 letras o más.
   - `Order.WrittenOffAmount` suma lo condonado y baja el saldo: `Balance = Total − PaidAmount − WrittenOffAmount`.
   - La venta, sus pagos y sus impuestos no cambian. No entra ni sale dinero de ninguna caja.
2. **Qué se condona** (`Order.WriteOff`): solo lo que un cliente debe por la regla de la 26A.
   - Una venta fiada, o un pedido entregado o terminado sin cobrar, que no esté cancelado.
   - Nunca lo que un repartidor cobra en efectivo: eso se liquida en Caja › Repartidores ([ADR 0019](0019-courier-cash-on-delivery.md)).
   - Nunca más que el saldo, mayor a cero y a centavos.
   - Un pago después de condonar cubre lo que queda, nunca más.
3. **Condonar desde la cuenta** (`WriteOffCustomerAccountCommand`).
   - Solo el dueño o un administrador (`RequiresTenantManager`): es una decisión sobre dinero que no entra.
   - Un monto con motivo, de un pedido de la cuenta o de toda la cuenta, del pedido más viejo al más nuevo, como un abono.
   - Corre dentro del candado del cliente de la 26A (`ICustomerCreditLock`). Así, un abono, una venta fiada y una condonación al mismo tiempo se cuentan entre sí.
   - Lleva lo que la cuenta debía en la pantalla (`ExpectedOwed`, obligatorio). Si llega dos veces, o después de un abono que la pantalla no vio, encuentra la cuenta cambiada y se rechaza.
   - Queda en la actividad del negocio (`CustomerAccountWrittenOff`) con el monto, el motivo y el reparto por folio. La nota queda en el registro del pedido, no en la actividad.
   - No se deshace en la 26B. Un error se corrige hacia adelante, y la pantalla lo dice antes del botón.
4. **El plazo del fiado** (`TenantSettings.CreditTermDays`).
   - Es uno por negocio: de 1 a 120 días, o sin plazo (null, lo que tienen todos al migrar).
   - Lo pone el dueño o un administrador (`UpdateCustomerCreditTermCommand`), y queda en la actividad (`CustomerCreditTermChanged`).
   - Se cuenta en días del negocio, en su zona horaria (`CreditTerm.Settings`): el último día para pagar es el día de la venta más el plazo, y se vence al día siguiente. Una venta de las 23:30 cuenta como de ese día, aunque en UTC ya sea otro.
   - Solo dice qué está vencido: no cobra intereses ni impide fiar.
5. **Lo vencido se ve:**
   - **Por cobrar:** lo vencido de cada cliente, el total vencido y «Solo vencidos» (quien tiene algo vencido, con todo lo que debe).
   - **Estado de cuenta:** lo vencido; en cada pedido, el día límite («Vence el…» o «Venció el…») y lo condonado; y lo último condonado. El resumen para WhatsApp dice lo vencido.
   - **Vender:** al fiar, el panel avisa «Tiene $X vencido». El cajero decide.
6. **Todo lo que calculaba un saldo** resta lo condonado:
   - lo que se debe (`Receivables`) y la ficha del cliente;
   - el tablero de Inicio («por cobrar»);
   - las cuentas de mesa;
   - la lista de Pedidos y el pedido;
   - lo que debe un repartidor;
   - lo que ve el cliente de su pedido («Por pagar»).
7. **El pedido dice lo que se condonó:** Pedidos muestra «Condonado» con su fecha, motivo, nota y quién lo hizo.
8. **El CFDI va por el total.** El borrador deja lo condonado como no pagado (PPD si queda algo sin pagar) y avisa que eso requiere una nota de crédito (CFDI de egreso), que TienditApp todavía no genera.
9. **Entregar sin cobrar sigue igual.** Pide `ManageOrders`, no `ChargeOrders`, y no revisa el límite:
   - el mesero y la cocina entregan antes de que se cobre;
   - el historial de estados dice quién lo entregó;
   - ahora el dueño puede ver lo vencido y condonarlo si no se cobra.
10. **Sin intereses.** Cobrar intereses por fiado tiene reglas propias (tasas, avisos al cliente, impuestos) y ningún negocio lo pidió.
11. **La migración** `20261020003342_AddCustomerWriteOffs` agrega:
    - la tabla `OrderWriteOffs` (vacía), con `CK_OrderWriteOffs_Amount` (mayor a cero);
    - `Orders.WrittenOffAmount` (`decimal(18,2)`, 0 por omisión), con `CK_Orders_WrittenOffAmount` (no negativo);
    - `TenantSettings.CreditTermDays` (`int`, con nulos), con `CK_TenantSettings_CreditTermDays` (de 1 a 120).

## Consecuencias

- **Lo que logra:**
  - Una deuda que no se va a cobrar sale de Por cobrar sin borrar nada: queda quién la perdonó, cuánto y por qué.
  - La caja, los cobros y las ventas no cambian. Lo condonado no es un pago ni un reembolso.
  - El negocio ve qué está atrasado y a quién cobrarle primero, y el cajero lo ve antes de fiar más.
- **Lo que limita:**
  - Los reportes no tienen todavía una vista de lo condonado en un periodo. Está en cada pedido, en la cuenta del cliente y en la actividad.
  - Sin nota de crédito: lo condonado de una venta facturada se resuelve fuera de TienditApp, con el contador.
  - Un solo plazo para todo el negocio. Un plazo por cliente y los recordatorios automáticos quedan para la 26C; el saldo a favor (anticipos), para la 26D.
  - No hay «deshacer»: un error se compensa hacia adelante.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (26A) funciona con la base migrada: no conoce la tabla ni las columnas.
  - Esa versión no resta lo condonado: lo ve otra vez como saldo y dejaría cobrarlo. Si ya se condonó algo, hay que volver a esta versión cuanto antes.
  - Un pago que la versión anterior acepte por encima del saldo real deja ese pedido con saldo negativo. Este código lo trata como pagado: no se debe, no se condona y no acepta otro pago.
- **Reversibilidad:**
  - `rollback.sql` borra la tabla, las columnas y sus restricciones solo si ningún negocio condonó nada ni puso plazo.
  - En cuanto alguno lo hace, se detiene con el error 50027 sin cambiar nada, y solo queda corregir hacia adelante.
