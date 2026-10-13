# ADR 0020: Servicios a domicilio

- Estado: Aceptada
- Fecha: 2026-10-13
- Continúa: [ADR 0017](0017-delivery-zones.md) (zonas de entrega) y [ADR 0018](0018-home-delivery-orders.md) (el envío como concepto propio con su IVA).

## Contexto

Un negocio de servicios sin local va a la casa de sus clientes: la estética que corta en casa, el técnico que da mantenimiento a una cafetera, quien hace limpiezas. Hasta la Fase 22D la Agenda solo sabía de citas en el negocio:

- la dirección se escribía en las notas y el cargo por ir se cobraba de memoria;
- nada impedía agendar a la misma persona en dos casas con cinco minutos de diferencia;
- quien salía a dar el servicio no tenía la lista de sus visitas del día en orden;
- la venta no sabía qué cita cobraba, así que una cita podía cobrarse dos veces o ninguna.

Las zonas de entrega (Fase 22B) ya dicen a qué códigos postales llega el negocio y cuánto cuesta ir, y el pedido a domicilio (Fase 22C) ya cobra el envío como concepto propio con su IVA.

## Decisión

1. **La visita es un hecho de la cita, no otra agenda.** `AppointmentVisit` (1:1 con la cita, `ITenantScoped`, índice único por cita, se carga siempre con ella) guarda el código postal, la dirección (hasta 300), las referencias (hasta 500), la zona y el cargo por visita acordado. Valida la dirección con las mismas reglas que el pedido a domicilio (`DeliveryAddress`). No guarda tasa de IVA: el IVA se decide al cobrar, como el envío.
   - `Appointment.ArrangeVisit` toma la zona activa que llega al código postal; el cargo es el costo de envío de la zona. El pedido mínimo y el «envío gratis desde» no aplican: una cita no tiene monto de productos.
   - Al editar, la misma dirección (código postal, calle y referencias) conserva la zona y el cargo: lo acordado con el cliente, aunque la zona cambie de costo después. Otra dirección se cotiza otra vez. Sin visita, `MoveToBusiness` la quita.
   - Solo en una cita abierta. La bitácora `AppointmentVisitArranged` y `AppointmentVisitRemoved` guarda la zona y el cargo, nunca la dirección.
2. **Las zonas son las de entrega.** Un negocio tiene una sola cobertura: a dónde va y cuánto cuesta ir. Para una cita, el costo de la zona es el cargo por visita.
   - Sin zonas activas no hay citas a domicilio: «Para citas a domicilio, primero da de alta tus zonas en Configuración › Zonas de entrega.».
   - Un código postal fuera de las zonas: «Por ahora no hacemos visitas en el 72000.».
   - La Agenda cotiza al escribir el código postal (`GET /appointments/visit-quote`), con lo que el servidor diría al guardar.
3. **Tiempo de traslado por negocio** (`TenantSettings.HomeVisitTravelMinutes`, de 0 a 240, 30 por omisión). Entre una visita y otra cita de la misma persona pasan al menos esos minutos: «Luis necesita 30 minutos de traslado entre una visita a domicilio y su otra cita.».
   - Se revisa en Application después del traslape, como el traslape: una visita contra cualquier cita de esa persona; una cita en el negocio solo contra sus visitas. Cuenta por su id del equipo o, en citas escritas a mano, por su nombre. Sin persona asignada no se revisa; 0 lo apaga.
   - Se configura en Configuración › Zonas de entrega, en la tarjeta «Visitas a domicilio», que ve el negocio con Agenda: `GET` y `PUT /delivery-zones/home-visits`. Lo cambia el dueño o un admin y lo lee cualquiera del equipo. Las citas ya agendadas no cambian.
4. **Ruta del día** (`GET /appointments/route?date=&staffUserId=`, quien maneja la agenda): las visitas de un día en la zona horaria del negocio, sin canceladas ni «no se presentó», por persona y en orden de hora, sin asignar al final, con el total de cargos de cada persona.
   - La Agenda la muestra en la vista «Ruta del día» con el día y la persona; cada visita con «Abrir en Maps», «Llamar» y «Cobrar», y cada persona con «Ruta en Maps» (las primeras 10 visitas).
   - El orden es la hora: la persona elige el camino en Maps. La dirección sale del dispositivo solo cuando alguien abre un enlace.
5. **El cargo por visita se cobra con la venta de la cita.** «Cobrar» en la Agenda abre Vender con la cita: su cliente, su servicio del catálogo (si lo tiene) y «Cargo por visita (zona)», que pone el servidor. La venta lleva `AppointmentId` (`CreateOrderCommand`):
   - Solo una venta de mostrador del negocio: ni de plataforma, ni cuenta de mesa, ni mesa, ni a domicilio, ni capturada sin conexión.
   - La cita existe en el negocio, no está cancelada ni «no se presentó» y no tiene ya su venta; si su venta se canceló, se cobra otra vez.
   - `Appointment.RecordSale` guarda la venta (`SaleOrderId`) y la da por completada.
   - Si es a domicilio con cargo, `Order.ChargeVisit` agrega `OrderVisitCharge` (1:1 con la venta, `ITenantScoped`): la cita, la zona, el cargo y la tasa del negocio al cobrar. Es un concepto más del desglose con la política de IVA del pedido, como el envío. Solo en una venta nueva, con productos, antes de cobrar.
   - La cita gana una versión (`rowversion`): dos cobros a la vez, o un cobro y una edición, dan 409 `concurrency_conflict`. Cambiar solo la visita también cambia la versión de la cita.
   - CFDI: el concepto «Cargo por visita a domicilio» lleva la clave del SAT del primer servicio de la venta (o 01010101), la unidad E48 y un aviso para confirmarla con el contador.
   - El ticket dice «Cargo por visita», y el detalle del pedido «Incluye el cargo por visita a domicilio (zona)». `OrderCreated` guarda el cargo.
6. **Los avisos de WhatsApp dicen dónde y cuánto, no la dirección.** Agendada, reagendada y confirmada dicen «a domicilio (CP 72000) con …» y «Cargo por visita: $80.00» (más IVA si los precios no lo incluyen). Cambiar el código postal avisa como un cambio de horario. La calle no va en el texto: queda en el registro de mensajes.
7. **Las respuestas automáticas** de un negocio con Agenda y sin Comida hablan de visitas: «Sí llegamos al 72000: el cargo por visita es de $80.00.», sin pedido mínimo ni envío gratis.

## Consecuencias

- **Lo que logra:**
  - La cita guarda dónde es y cuánto cuesta ir, y el cliente lo sabe desde el aviso.
  - La agenda deja el tiempo de traslado entre una casa y otra.
  - Quien sale tiene su ruta del día con mapas y teléfonos.
  - Cada cita se cobra una vez, con su cargo por visita como concepto propio con su IVA.
- **Lo que limita:**
  - La ruta no se optimiza: va en orden de hora y la persona decide el camino en Maps.
  - El traslado es un tiempo fijo por negocio, no la distancia real; no hay mapa propio ni geocodificación.
  - No se agenda desde el menú en línea (hoy no hay agenda en línea).
  - Las direcciones guardadas del cliente no tienen código postal: la dirección se escribe en la cita.
  - Dos citas guardadas al mismo tiempo para la misma persona pueden traslaparse: la base no lo impide, como antes.
  - Dos ediciones simultáneas de una cita ahora dan 409 en lugar de quedarse con la última.
- **Decisión por omisión:** se tomó sin una preferencia expresa del dueño del producto, con su autorización para decidir:
  - usar las zonas de entrega, no otra cobertura, porque a dónde va el negocio es lo mismo para un pedido y para una visita;
  - cobrar el cargo con la venta de la cita, no al agendar, porque la cita todavía puede cambiar o cancelarse;
  - no repetir la calle en los avisos, porque el texto queda en el registro de mensajes.
- **Reversibilidad:**
  - La migración `AddHomeVisits` agrega `HomeVisitTravelMinutes` (30 por omisión) a `TenantSettings`, `SaleOrderId` (con nulos) y la versión a `Appointments`, y dos tablas vacías: `AppointmentVisits` y `OrderVisitCharges`.
  - Volver a la versión anterior de la app no necesita la base: no conoce las columnas ni las tablas, y cada venta conserva su total guardado, cargo incluido. Pero no muestra las visitas ni el cargo, y vuelve a guardar la última edición de una cita sin revisar la versión.
  - El `rollback.sql` se detiene con `THROW 50015` mientras haya visitas, cargos por visita o citas cobradas: se perderían direcciones, cargos y ventas. Sin ellos, quita lo que agregó, y correrlo otra vez no hace nada.
