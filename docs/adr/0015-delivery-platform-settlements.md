# ADR 0015: Liquidación de los depósitos de cada plataforma

- Estado: Aceptada
- Fecha: 2026-10-12

## Contexto

Desde la Fase 22F ([ADR 0013](0013-delivery-platform-orders.md)), cada pedido de Rappi, Uber Eats o DiDi Food copia su comisión, el IVA de la comisión, las retenciones y el neto esperado (`PlatformOrderDetail.ExpectedSettlement`). Desde la 22F-2 ([ADR 0014](0014-delivery-platform-prices.md)) los calcula con los precios de la app. Cada semana la plataforma deposita, o cobra, la suma de esos netos, pero el depósito casi nunca es exacto:

- descuenta la parte de las promociones que paga el negocio y suma la que cubre ella (el 35 % que cubre Rappi);
- descuenta los faltantes y los reclamos de los clientes;
- descuenta o paga las cancelaciones tardías;
- cobra cargos propios y arrastra saldos de semanas anteriores (el saldo negativo de DiDi por los pedidos que el negocio cobró en efectivo).

Hasta ahora TienditApp solo lista los pedidos por folio para compararlos con el estado de cuenta (`GET /reports/delivery-platform-orders`, de solo lectura). No hay dónde decir qué depositó cada plataforma, qué falta por cobrar ni por qué un depósito no cuadra.

Las decisiones fijas de la fase eran:

- Captura manual: no hay API de las plataformas.
- Cada peso del depósito se explica.
- Las correcciones compensan, no borran.
- Los términos copiados en cada pedido nunca se recalculan.
- Una migración que solo agrega, con su guarda de rollback.
- Disponible en todos los planes.

## Decisión

1. **Una liquidación por depósito, de una plataforma.**
   - **El agregado:** `PlatformSettlement` es `AuditableEntity`, `ITenantScoped` e `IAggregateRoot`. Guarda:
     - la plataforma, que no cambia;
     - el periodo, en fechas del negocio: de 1 a 31 días, sin llegar al futuro;
     - la fecha del depósito: desde el inicio del periodo y nunca en el futuro;
     - lo depositado, de −10,000,000 a 10,000,000 con dos decimales. Es negativo cuando la plataforma le cobra al negocio;
     - una referencia (hasta 60 caracteres) y notas (hasta 500);
     - el estado: `Open = 0`, `Closed = 1` o `Voided = 99`;
     - cuándo se cerró, y cuándo y por qué se anuló;
     - `Revision` como token de concurrencia;
     - la suma de sus pedidos y la de sus ajustes, que el agregado mantiene.
   - **Una abierta por plataforma:** un índice único filtrado `(TenantId, Channel) WHERE Status = 0`. Se concilia un depósito a la vez.
2. **Lo que espera cada pedido.**
   - **El neto esperado:** es R(`ExpectedSettlement` × `NetPaidAmount` / `OrderTotal`), redondeado una vez a centavos con `MidpointRounding.ToEven`. Es la parte que Ventas por canal le atribuye al pedido:
     - si cobró la plataforma, el total menos lo devuelto;
     - si cobró el negocio, solo lo que ya cobró, en negativo: es la comisión que la plataforma descontará;
     - 0 si se devolvió todo o si el negocio todavía no lo cobra.
   - **Pendiente:** un pedido de la plataforma que no está en una liquidación abierta ni cerrada y cuyo neto esperado no es 0.
   - **Un pedido, una liquidación:** `PlatformSettlementOrder` (`ITenantScoped`) une la liquidación y el pedido, copia el neto esperado y lleva `IsActive`. Un índice único filtrado `(OrderId) WHERE IsActive = 1` impide que un pedido esté en dos liquidaciones vigentes. Anular una pone `IsActive` en 0, y sus filas se quedan como historia.
   - **Qué pedidos lleva:** los que la persona elige, hasta 2,000, entre los pendientes de la plataforma registrados hasta el final del periodo (la fecha del negocio de `RegisteredAt`). La pantalla propone marcados los del periodo, y sin marcar los pendientes anteriores.
   - **Los números se recalculan:** el servidor recalcula cada neto esperado al guardar. Al cerrar los vuelve a calcular y, si uno cambió desde el último guardado (una devolución en medio), guarda los nuevos y responde 409 para que la persona los revise.
3. **Ajustes con motivo.**
   - **`PlatformSettlementAdjustment`** (`ITenantScoped`) guarda el motivo, una descripción y un importe con signo: negativo cuando la plataforma descuenta.
   - **Motivos:**

     | Motivo | Valor | Ejemplo |
     |---|---|---|
     | Promociones | `Promotion = 1` | La parte del descuento que paga el negocio, o la que cubre la plataforma |
     | Faltantes y reclamos | `Claim = 2` | Un producto que no llegó y la plataforma le devolvió al cliente |
     | Cancelaciones | `Cancellation = 3` | Un pedido ya entregado que la plataforma canceló después |
     | Cargos de la plataforma | `Fee = 4` | Publicidad, tableta, penalizaciones |
     | Saldo anterior | `PreviousBalance = 5` | El saldo negativo que DiDi arrastra de otra semana |
     | Otro | `Other = 99` | Con descripción obligatoria |

   - **Límites:** hasta 100 ajustes por liquidación. El importe no puede ser 0, va de −1,000,000 a 1,000,000 con dos decimales, y la descripción tiene hasta 200 caracteres.
4. **Cuadra o no se cierra.**
   - **Las cuentas:**
     - debió depositar = la suma de sus pedidos + la de sus ajustes;
     - diferencia = depositado − debió depositar.
   - **Cerrar** exige una diferencia de 0, al menos un pedido o un ajuste y la fecha del depósito sin llegar al futuro. Deja `PlatformSettlementClosed` en la bitácora en el mismo guardado. Una liquidación cerrada ya no cambia.
   - **Una diferencia que no se explica** se registra como un ajuste «Otro» con su nota. La pantalla lo hace en un clic.
5. **Anular, nunca borrar.**
   - Una liquidación abierta o cerrada se anula con un motivo obligatorio de hasta 500 caracteres.
   - Sus pedidos vuelven a estar pendientes, y deja `PlatformSettlementVoided` en la bitácora.
   - No hay forma de borrar una liquidación.
6. **Por cobrar.** Para cada plataforma activa o con pedidos pendientes: cuántos pedidos están pendientes, la suma de sus netos esperados (cada uno redondeado como en la liquidación), la fecha del más antiguo y la liquidación abierta, si hay una.
7. **API** (`api/v1/platform-settlements`):
   - **Lecturas:**
     - `GET /`: la lista, la más nueva primero, por plataforma y estado, en páginas;
     - `GET /{id}`: el detalle con sus pedidos, sus ajustes, las cuentas y los demás pedidos pendientes hasta el final del periodo;
     - `GET /pending-orders?channel=&from=&to=`: los pendientes para una liquidación nueva, cada uno con si cae en el periodo;
     - `GET /receivable`: lo que hay por cobrar.
   - **Cambios:**
     - `POST /` crea una abierta;
     - `PUT /{id}` la reemplaza entera con su revisión, como el guardado de Precios en apps;
     - `POST /{id}/close` la cierra con su revisión;
     - `POST /{id}/void` la anula con su revisión y el motivo.
   - **Respuestas:**
     - 400 `validation_failed` o `business_rule`;
     - 404 si la liquidación no es del negocio;
     - 409 `concurrency_conflict` con una revisión vieja o cuando otra liquidación de la plataforma se abrió al mismo tiempo: alguien más guardó, y la pantalla ofrece ver lo guardado;
     - 409 `conflict`, con el motivo en `detail`, cuando otra liquidación tomó uno de los pedidos o cuando, al cerrar, cambió lo que se espera de un pedido (el servidor guarda las cifras nuevas).
8. **Permisos.**
   - **Leer:** `ViewReports`, igual que Ventas por canal, donde ya se ven la comisión y el neto.
   - **Crear, guardar, cerrar y anular:** dueños y administradores (`[RequiresTenantManager]`), igual que los términos de cada plataforma.
   - Sin capability ni módulo de plan.
9. **Reportes › Liquidaciones.**
   - Reportes pasa a ser una sección con dos pestañas: «Ventas», la página de siempre, y «Liquidaciones».
   - Liquidaciones muestra lo que hay por cobrar de cada plataforma y la lista de liquidaciones.
   - El detalle (`/reports/settlements/{id}`, o `new` con la plataforma) es una pantalla de detalle, con migas.
10. **Lo demás no cambia.** Ventas por canal, Inicio, el corte y el resumen diario siguen en base caja: lo que cobró la plataforma cuenta al registrar el pedido, y la liquidación solo concilia lo que la plataforma le debe al negocio. Un depósito no es un movimiento de caja: TienditApp no lleva cuentas de banco.
11. **Una migración que solo agrega, con la guarda 50010.**
    - `AddPlatformSettlements` crea tres tablas y no toca ninguna otra. Su `apply.sql` corre en producción antes del merge.
    - Su `Down` y su `rollback.sql` se detienen con el error 50010 si ya hay liquidaciones: ninguna bitácora guarda sus pedidos ni sus ajustes.

## Alternativas descartadas

- **Importar el estado de cuenta (CSV o PDF):** cada plataforma usa un formato distinto y lo cambia sin aviso. La captura manual funciona con las tres, y la importación puede llegar después sobre el mismo modelo.
- **Capturar el neto real de cada pedido:** obligaría a escribir cientos de importes por semana. Marcar los pedidos que trae el estado de cuenta y explicar la diferencia con ajustes deja la misma cuenta.
- **Cerrar con una diferencia sin explicar:** escondería los errores, y la siguiente semana nadie sabría de dónde salió. Registrarla como «Otro» con una nota cuesta un clic.
- **Editar o borrar una liquidación cerrada:** rompería la regla de que las correcciones compensan. Se anula y se registra otra.
- **Recalcular el neto con los términos de hoy:** los términos copiados en cada pedido nunca cambian ([ADR 0013](0013-delivery-platform-orders.md)).
- **Ligar sola una devolución posterior a la siguiente liquidación:** obligaría a que un pedido esté en dos liquidaciones vigentes. Por ahora se registra como un ajuste «Cancelaciones».
- **Un permiso nuevo para liquidar:** hoy concilian los dueños y administradores, como con los términos. Un permiso propio puede llegar si un negocio delega la tarea.
- **Varias liquidaciones abiertas de una plataforma:** dos borradores se pelearían los mismos pedidos.

## Consecuencias

- **Lo que logra:**
  - Cada depósito queda registrado con sus pedidos y la explicación de cada peso que no cuadra.
  - «Por cobrar» dice cuánto debe cada plataforma y desde cuándo.
  - Un pedido nunca se cobra dos veces, y un error se corrige anulando, sin perder la historia.
- **Lo que limita:**
  - **Devoluciones posteriores:** una devolución de un pedido ya liquidado no aparece sola en la siguiente liquidación. La plataforma la descuenta de otro depósito, y la persona la registra como un ajuste «Cancelaciones».
  - **Centavos:** la liquidación redondea por pedido y Ventas por canal por fila, así que pueden diferir por centavos.
  - **El periodo** sigue la fecha en que se registró cada pedido, no la de entrega ni el corte de cada plataforma (Uber Eats corta los lunes a las 4:00). Los pedidos de la orilla se marcan o desmarcan a mano.
  - **El folio corto** sigue siendo la única llave para comparar con el estado de cuenta.
  - **Hasta 2,000 pedidos** por liquidación, unos 285 al día en una semana.
- **Reversibilidad:**
  - **Volver a la versión anterior** funciona sin `rollback.sql`: no conoce las tablas, y las liquidaciones se quedan guardadas sin efecto.
  - **Con liquidaciones guardadas,** `rollback.sql` se detiene con el error 50010. Para quitar las tablas hay que exportarlas y borrarlas primero, o corregir hacia adelante ([migraciones](../development/database-migrations.md)).

## Siguientes pasos

- **Importar el estado de cuenta** de cada plataforma.
- **El ID largo del pedido** para comparar con el estado de cuenta.
- **Ligar las devoluciones posteriores** a la siguiente liquidación.
- **CFDI de retenciones** que emiten las plataformas.
