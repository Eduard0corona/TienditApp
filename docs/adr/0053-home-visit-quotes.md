# ADR 0053: Oficios a domicilio, la visita para cotizar

- Estado: Aceptada para la etapa 1 y la política de la visita (punto 10) en la Fase 29A (2026-10-31), para la solicitud en línea (etapa 2) en la Fase 29B (2026-11-01) para la cotización (etapa 3, puntos 14, 15 y 17) en la Fase 29C (2026-11-01) y para su envío desde TienditApp (punto 16) en la Fase 29D (2026-11-01); convertir la aceptada en el trabajo sigue propuesto para la Fase 29E.
- Fecha: 2026-10-27
- Continúa: [ADR 0020](0020-home-visits.md) (servicios a domicilio), [ADR 0021](0021-business-directory.md) (Explorar) y [ADR 0025](0025-online-booking.md) (citas en línea).

## Contexto

Plomeros, electricistas, técnicos de aire acondicionado y de refrigeración, fumigadores, cerrajeros y oficios parecidos no venden en mostrador: van a la casa del cliente, revisan, dicen cuánto cuesta y después hacen el trabajo. Quien los busca no pregunta si puede comprar ahora, sino si llegan a su colonia, cuánto cuesta que vengan y cuándo pueden.

Lo que TienditApp hace hoy con ellos (revisado en `master` en `4a03d292`):

- **Su menú queda vacío.** Las plantillas de Carpintería, Herrería y soldadura, Electricista, Plomería y Aire acondicionado (`StarterCatalogTemplates`) son servicios, por servicio o por hora, y el menú en línea no muestra servicios porque no se piden en línea (`PublicDirectorySnapshot.MenuProducts`).
- **Explorar solo los ve como tienda o como local con agenda.**
  - Como tienda, «Pide en línea» exige menú con pedidos, estar abierto por horario y una caja abierta en la sucursal principal (`OnlineOrderAvailability`, [ADR 0021](0021-business-directory.md)). Sin caja dice «Pedidos en línea en pausa», y sin pedidos «Solo menú».
  - Como agenda, las citas en línea exigen local (`OnlineBookingBlocker.NoPremises`): son en el negocio, no a domicilio ([ADR 0025](0025-online-booking.md)).
  - Un oficio sin productos y sin citas en el local no aparece. Uno con algún producto aparece con palabras de tienda que no le corresponden, y su lugar en la lista depende de la caja.
- **Nace con su dirección pública.** Solo el giro «Comida a domicilio» empieza con la sucursal principal «Solo a domicilio» (`BusinessIndustries.OnlyDelivers`). Un plomero empieza con local (`Premises`): su tienda publica la dirección, muchas veces su casa, y si prende las citas en línea el cliente agenda en ella.
- **No hay cotización.** Las plantillas traen «Visita y diagnóstico» y «Visita para medir y cotizar», y la Agenda ya cobra la visita con el cargo de su zona ([ADR 0020](0020-home-visits.md)), pero nada guarda lo que el negocio propone después de revisar ni si el cliente lo aceptó: se pasa directo a la venta.
- **Faltan giros:** fumigación, refrigeración y línea blanca, cerrajería, jardinería, limpieza, pintura e impermeabilización.

Lo que ya existe y se reutiliza:

- zonas por código postal con su cargo por visita, tiempo de traslado y ruta del día ([ADR 0020](0020-home-visits.md));
- solicitudes «Por confirmar» con enlace y aviso por WhatsApp, horas libres que calcula el servidor y elegir con quién ([ADR 0025](0025-online-booking.md) y [ADR 0032](0032-online-booking-staff.md));
- fotos del antes y del después en la orden de servicio ([ADR 0024](0024-service-photos.md));
- correo por SMTP desde el outbox ([ADR 0049](0049-cfdi-email.md)).

Las decisiones del dueño del producto (2026-10-27):

1. Un interruptor del negocio, no una regla del giro.
2. A los negocios ya registrados con su dirección pública se les avisa; no se les cambia nada.
3. Una categoría nueva en Explorar: «Hogar y reparaciones».
4. El cliente elige una hora exacta, no una franja.
5. El negocio decide cómo cobra la visita.
6. La cotización le llega al cliente por WhatsApp y por correo.

## Decisión

Un tercer modo de atender en línea, junto a los pedidos (la tienda) y las citas en el local: **la visita para cotizar**. Lo deciden la cobertura y el horario del negocio, nunca la caja. La caja solo entra al cobrar, como hoy.

### Etapa 1: que se vean como lo que son

1. **«Visitas a domicilio para cotizar»**, un interruptor del negocio (`TenantSettings.HomeVisitRequestsEnabled`, apagado por omisión). Lo cambia el dueño o un administrador, queda en la actividad, y funciona cuando además el negocio:
   - tiene el módulo de agenda;
   - publicó su menú en línea;
   - tiene al menos una zona de entrega activa: a dónde va y cuánto cuesta ir;
   - tiene horario semanal, del que salen las horas libres.

   Como «Citas en línea», su tarjeta en Configuración dice cuál falta. No pide local.
   - Un negocio que se registra con un giro de oficio empieza con el interruptor prendido. Sin zonas ni horario no recibe nada, y la guía de inicio dice qué falta.
2. **La categoría «Hogar y reparaciones»** (`BusinessCategory.HomeRepairs = 11`), con la migración que amplía `CK_Tenants_DirectoryCategory`.
   - La sugieren los giros de oficio (`BusinessCategoryDefaults`): Carpintería, Herrería y soldadura, Electricista, Plomería, Aire acondicionado y los nuevos.
   - Un negocio cuya categoría sigue a su giro (nadie la decidió, [ADR 0021](0021-business-directory.md)) se mueve con él. Uno cuyo dueño la decidió la conserva.
3. **Giros nuevos con su plantilla:** Fumigación, Refrigeración y línea blanca, Cerrajería, Jardinería, Limpieza del hogar, y Pintura e impermeabilización. Cada plantilla trae su servicio de visita y sus servicios con duración.
4. **Sin dirección pública desde el registro.** Los giros que normalmente no tienen local empiezan con la sucursal principal «Solo a domicilio» ([ADR 0016](0016-delivery-only-branches.md)), como «Comida a domicilio». Carpintería y Herrería y soldadura conservan el local, porque suelen tener taller. El dueño lo cambia en Sucursales.
5. **Aviso a los que ya están registrados** (decisión 2). Un negocio con el interruptor prendido o con giro de oficio, cuya sucursal principal tiene local, lo ve en «Cómo te ven en TienditApp», en la tarjeta del interruptor y en «Requiere atención» de Inicio: «Tu tienda muestra la dirección de tu sucursal principal. Si no atiendes en un local, márcala «Solo a domicilio» en Sucursales.». Se descarta con un clic, y nada cambia sin el dueño.
6. **La tarjeta de Explorar** de un negocio que recibe visitas habla de visitas, nunca de pedidos ni de caja:
   - «Visitas a domicilio» en lugar de «Pide en línea», «Pedidos en línea en pausa» o «Solo menú»;
   - el costo de la visita con su política (punto 10): «Visita $350», «Visita sin costo» o «Visita $350, se descuenta si contratas»;
   - con el código postal del cliente, «Va a tu código postal» o «No va a tu código postal», con la misma consulta de zonas que el envío ([ADR 0022](0022-shopping-with-an-account.md));
   - su horario y sus reseñas, como hoy.
7. **Su lugar en Explorar no depende de la caja.** Va con los que reciben pedidos y los que agendan en línea: el cliente pide la visita para una hora libre aunque el negocio esté cerrado ahora. «Cerca de mí» no lo mide, porque no publica su punto. Con el código postal del cliente, primero los que van a él.
8. **Aparece aunque su menú no tenga productos**, como un negocio que agenda en línea: la regla de `PublicDirectorySnapshot` suma «recibe visitas».
9. **Su tienda (`/m/{slug}`)** no pone sus servicios en un carrito: muestra «Solicitar visita», sus servicios como precios de referencia («desde $500»), los códigos postales a los que va (sin el nombre de la zona) y su política de la visita. Si además vende productos, su menú sigue como hoy.

### Lo que hizo la Fase 29A

La etapa 1 completa y, de la etapa 2, la política de la visita (punto 10), porque la tarjeta y la tienda ya dicen cuánto cuesta. La migración es `20261031085333_AddHomeVisitRequests`, con sus scripts en `database/production/20261031085333_AddHomeVisitRequests/`.

- **La regla del servidor** (`HomeVisitRequestAvailability`) decide si un negocio recibe visitas: el interruptor prendido, el módulo de agenda, el menú publicado, al menos una zona activa y horario semanal. La usan la tarjeta del dueño, Explorar, la tienda, «Cómo te ven» y la guía de inicio.
- **Configuración › «Visitas a domicilio»** (dueños y administradores, con el módulo de agenda):
  - el interruptor, el servicio que es la visita, cómo se cobra, lo que lee el cliente y lo que falta, en las palabras del servidor;
  - `GET` y `PUT /api/v1/appointments/home-visit-requests`;
  - el servicio debe poder agendarse (disponible y en una categoría activa). Uno que ya no se agenda se conserva hasta que el dueño elige otro, y no suma nada a la visita;
  - cada cambio queda en la actividad (`HomeVisitRequestsUpdated`).
- **Lo que cuesta la visita lo calcula el servidor** (`HomeVisitCharge`):
  - el precio del servicio más el cargo de la zona;
  - sin servicio, solo el cargo de la zona;
  - «Sin costo» deja la visita en cero;
  - antes de saber el código postal, desde la zona que cobra menos: «desde» cuando las zonas cobran distinto;
  - se escribe como los precios del negocio: «más IVA» cuando no lo incluyen.
- **La categoría «Hogar y reparaciones»** (11) tiene su página en `/explorar/hogar-y-reparaciones`. Para los buscadores es `HomeAndConstructionBusiness`.
  - La migración mueve a ella los oficios cuya categoría sigue su giro: la que el giro sugería antes de la migración, sin un cambio del dueño en la actividad (`DirectoryProfileUpdated`). Los cinco de antes estaban en Servicios; los nuevos, en la de su tipo.
  - El rollback los regresa a esa categoría y se detiene (50049) si alguno la eligió después.
  - Un oficio que se registra entre `apply.sql` y el despliegue toma la categoría que sugería el código anterior. `database/production/data-repair/20261031_HomeRepairsAfterDeploy/` lo mueve después del despliegue, con la misma regla; se puede correr más de una vez.
- **El registro:**
  - Un giro de oficio empieza con el interruptor prendido.
  - Todos menos Carpintería y Herrería y soldadura empiezan con la sucursal principal «Solo a domicilio».
  - Cada plantilla marca su servicio de visita, que queda como la visita del negocio si no tenía una.
- **El aviso de la dirección publicada** sale una vez en Configuración, solo si la sucursal principal tiene local y publica una calle o un punto en el mapa:
  - En «Cómo te ven en TienditApp», y no en la tarjeta del interruptor: las dos están en la misma pantalla, y «Cómo te ven» la ve también un oficio sin el módulo de agenda.
  - En Inicio, una línea de «Requiere atención» (`homeVisitAddress`, solo para dueños y administradores) lleva ahí.
  - «Ya lo revisé» lo quita para siempre: `POST /api/v1/auth/my-tenant/directory/home-visit-address-notice/dismiss`, sin pedir el módulo de agenda.
- **La guía de inicio** de un negocio con el interruptor y el módulo de agenda tiene un paso más: «Prepara tus visitas a domicilio», listo cuando ya recibe visitas.
- **En Explorar:**
  - la tarjeta dice «Visitas a domicilio», el costo de la visita y, con el código postal, si va ahí y cuánto cuesta la visita en él;
  - su horario sigue, pero nunca «Pedidos en línea en pausa» ni «Solo menú»;
  - con el código postal, los negocios que reciben visitas y no van a él pasan al final de la página. El orden de los demás, incluido el envío, no cambia. Para ordenar se leen solo las zonas de los que reciben visitas; cada página dice el envío y la visita con las zonas de sus negocios.
- **La tienda** muestra «Visitas a domicilio»:
  - el costo y la política de la visita;
  - los códigos postales a los que va: hasta 300, con cuántos son en total, nunca el nombre de la zona;
  - hasta 50 servicios como precios de referencia, «desde», sin carrito;
  - «Solicitar visita», que abría un WhatsApp con el negocio hasta la Fase 29B. Desde una mesa no sale.

### Lo que hizo la Fase 29B

La etapa 2 (puntos 11 a 13). La migración es `20261101002207_AddHomeVisitBooking`, con sus scripts en `database/production/20261101002207_AddHomeVisitBooking/`.

- **La página de la solicitud** (`/m/{slug}/visita`, sin cuenta), a la que lleva «Solicitar visita» de la tienda:
  - `GET /api/v1/public/menu/{slug}/home-visit`: la visita (el servicio que el negocio eligió mientras se agenda, o «Visita a domicilio» de una hora sin él), su duración, cómo se cobra, lo que cuesta desde la zona más barata y quién va. No existe mientras el negocio no recibe visitas (`HomeVisitRequestAvailability`).
  - `GET …/home-visit/coverage?postalCode=`: si va a ese código postal y cuánto cuesta la visita ahí, sin el nombre de la zona. Una cuenta empieza con su última dirección.
  - `GET …/home-visit/slots`: las horas de una semana con la duración de la visita. El traslado del negocio (`HomeVisitTravelMinutes`) queda libre antes y después de la visita, contra todas las citas de cada persona; las otras citas ocupan solo su tiempo, así que entre dos visitas el traslado se cuenta una vez, como en la agenda. Sin decir quién atiende, cuenta contra todas las del negocio con las citas a la vez, y una cita en el local también deja libre el traslado de las visitas que ya hay (antes solo lo hacía con quién atiende).
  - Con quién: los que agendan en línea y hacen el servicio de la visita; sin servicio, cualquiera de ellos. Si nadie de ellos hace la visita, se pide igual, sin nadie asignado y con las citas a la vez del negocio, y el negocio la asigna en la Agenda: la tienda nunca ofrece una visita que nadie puede tomar.
- **La solicitud** (`POST …/home-visits`, `Appointment.RequestHomeVisit`) es una cita «Por confirmar» con su visita, por el mismo camino que una cita en línea: la llave, el candado del negocio, el límite de 3 por confirmar por teléfono (citas y visitas juntas), el enlace, Mis citas y el aviso por WhatsApp. No pide local.
  - Lleva lo que pasa (las notas de la cita, hasta 500 caracteres), la dirección con sus referencias y el correo opcional del cliente (`Appointments.CustomerEmail`) para la cotización.
  - Lleva también cómo se cobra y cuánto cuesta la visita según la página: si el negocio cambió cualquiera de los dos desde que el cliente los vio, se rechaza y la página los lee otra vez, como el total de un pedido en línea.
  - La visita guarda cómo se cobra, como el cliente lo aceptó (`AppointmentVisits.ChargePolicy`). Su cargo es el de la zona; «Sin costo» lo deja en cero a donde se mueva, y la restricción `CK_AppointmentVisits_ChargePolicy` lo exige. Una visita que agenda el equipo queda sin política.
  - El servicio se cobra con su renglón al cobrar la cita; con «Sin costo», «Cobrar» no lo agrega.
- **La Agenda** decide la solicitud como cualquier otra y dice «Visita para cotizar» con cómo se cobra y el correo del cliente. Aceptada o movida, el aviso dice lo que cuesta la visita (el servicio más la zona) o que es sin costo.
- **El enlace y Mis citas** dicen «A domicilio · CP …», nunca la dirección, y ofrecen pedir otra visita.
- **El rollback** se detiene (50050) en cuanto hay una visita pedida en línea o un correo de cliente.

### Lo que hizo la Fase 29C

La cotización (puntos 14, 15 y 17), salvo convertir la aceptada en el trabajo. La migración es `20261101055834_AddQuotes`, con sus scripts en `database/production/20261101055834_AddQuotes/`. El envío desde el outbox (punto 16) y la conversión quedan para la Fase 29D.

- **`Quote`** (`Domain/Quotes`): cada versión es una fila con el folio de su cotización (`COT-000123`, de `IFolioGenerator`, que empieza después del más alto del negocio) y su número de versión.
  - Guarda el cliente (uno de la lista del negocio o su nombre y teléfono, y su correo), la cita de la visita, si sus precios incluyen IVA (la política del negocio al empezarla, que heredan sus versiones), la vigencia en días, una nota y sus renglones.
  - Un renglón es un servicio o producto del catálogo, con su IVA, o algo escrito a mano, con el IVA del negocio. El negocio pone la cantidad y el precio; el agregado calcula el importe de cada renglón, el subtotal, el IVA y el total con `OrderTaxCalculator`, como un pedido. El navegador nunca manda un total.
  - Borrar del catálogo el producto de un renglón, o de la lista al cliente, deja la cotización con lo que dice.
- **Sus estados** los cambia el agregado: Borrador, Enviada, Aceptada, Rechazada, Reemplazada y Descartada.
  - «Vencida» no se guarda: una enviada se lee vencida el día después de su vigencia, en el día del negocio. Así vence sola sin un proceso que la cambie. Vencida ya no se acepta ni se rechaza, pero se corrige.
  - Enviada ya no se edita. «Corregir» (de una enviada, vencida o rechazada) empieza la versión siguiente con lo que decía. Al enviarse, la anterior queda «Reemplazada» con lo que decía (una rechazada conserva cuándo respondió el cliente). Un folio tiene un solo borrador a la vez (`IX_Quotes_TenantId_Number_Draft`), y una corrección no se envía si el cliente ya aceptó la versión que tenía: aceptarla descarta la corrección que se había empezado.
  - Un borrador se descarta y queda en el historial: su folio y su versión no se vuelven a dar.
- **Enviar** fija la vigencia desde el día del negocio (15 días por omisión, `TenantSettings.QuoteValidityDays`, de 1 a 90; dueños y administradores la cambian en Cotizaciones y queda en la actividad) y le da a la versión su enlace. La pantalla ofrece mandarlo por WhatsApp (`wa.me` al teléfono del cliente con el folio, lo que hay que pagar, la vigencia y el enlace), por correo (`mailto:`) o copiarlo. Mandarlo desde el outbox, con su historial, es la Fase 29D.
- **El enlace** (`/cotizacion#…`, `POST /api/v1/public/quotes/view`) es como el de un pedido: va en el fragmento y abre solo esa versión, sin cuenta y con el límite de la tienda.
  - Del cliente solo dice su nombre.
  - Deja de abrir 30 días después de vencer.
  - Una versión reemplazada lo dice y lleva a la nueva.
- **«Visita (se descuenta)»**: con «Se descuenta si contratas» (la política con que se pidió la visita; si la agendó el equipo, la del negocio), la cotización de una visita cobrada resta lo que su venta cobró por la visita.
  - Es el cargo de la zona y las unidades del servicio de la cita que el cliente conservó (sin las devueltas), con su IVA, y nunca más de lo que esa venta tiene pagado.
  - Se recalcula en el borrador y se fija al enviarla.
  - Un reembolso o una devolución posterior de esa venta (que la deja completada) baja lo que resta a lo que la venta sigue teniendo pagado por la visita.
  - Si la venta se cancela o se reembolsa completa, deja de aplicar.
  - Se resta del total: «Por pagar».
- **La respuesta**: el negocio registra si el cliente la aceptó o la rechazó, por dónde (WhatsApp, correo, teléfono o en persona) y lo que dijo, hasta 500 caracteres. Lo que dijo queda en la cotización. La actividad guarda el folio, la versión, el total y por dónde, nunca lo que dijo.
- **Quién**: quien toma pedidos (`ManageOrders`) en un negocio con órdenes de servicio o agenda. Cotizaciones es una pestaña junto a Pedidos.
  - «Cotizar» en la Agenda abre la cotización de una visita (abierta o completada): una nueva con su cliente, o la que ya tiene. El servidor no cotiza una cita en el local, una cancelada, una a la que el cliente no llegó ni una solicitud por aceptar, y una visita tiene una sola cotización también con dos «Cotizar» a la vez (`IX_Quotes_TenantId_AppointmentId_First`).
  - En la pantalla, «Terminar y obtener enlace» la deja enviada: TienditApp todavía no se la manda al cliente.
- **El rollback** se detiene (50051) en cuanto hay una cotización o un negocio con otra vigencia.

### Lo que hizo la Fase 29D

El envío de la cotización desde TienditApp (punto 16). La migración es `20261101181148_AddQuoteDeliveries`, con sus scripts en `database/production/20261101181148_AddQuoteDeliveries/`. Convertir la aceptada en el trabajo queda para la Fase 29E: una orden solo lleva renglones del catálogo con su precio, y la cotización tiene renglones escritos a mano con su precio y su IVA, así que primero hay que decidir cómo entra un renglón sin producto.

- **Solo se manda una versión enviada y vigente** en el día del negocio: una aceptada, rechazada, reemplazada o vencida ya no es lo que el negocio ofrece. Se revisa al pedirlo y otra vez al salir: un correo o un WhatsApp que se reintentó hasta después de que la versión dejó de valer no sale (el correo termina como «ya no vigente»; el WhatsApp, como no enviado).
- **«Enviar por correo»** (`POST /api/v1/quotes/{id}/email`) guarda un `QuoteEmail` con a quién y quién lo pidió, y el outbox lo manda después (`QuoteEmailRequestedEvent`), como el correo de un CFDI ([ADR 0049](0049-cfdi-email.md)).
  - Sale de la plataforma como «{Negocio} vía TienditApp», con `Reply-To` al correo del negocio, sin adjuntos: en el cuerpo, la cotización como el cliente la lee en su enlace (renglones, IVA, la visita que se descuenta, lo que falta pagar, hasta cuándo vale, la nota) y el enlace. Todo lo que escribieron el negocio o el cliente va codificado.
  - Un servidor que no responde se reintenta con el outbox; uno que rechaza la dirección, o sin correo configurado, lo termina; pasado el último intento termina sin entregarse. Cada correo guarda su resultado.
- **«Enviar por WhatsApp»** (`POST /api/v1/quotes/{id}/whatsapp`), cuando el negocio conectó su WhatsApp, encola un `OutgoingMessage` con el propósito `QuoteDocument` (6) y la versión que lleva. Sale del número del negocio por la cola de mensajes, que lo reintenta y oye si se entregó y se leyó.
  - Va con la plantilla que el negocio eligió para sus cotizaciones en Cotizaciones (`TenantSettings.QuoteWhatsAppTemplateId`, dueños y administradores; una plantilla «Cotización por WhatsApp» lleva `{enlace}` y puede decir `{nombre}`, `{negocio}`, `{folio}`, `{monto}`, lo que paga el cliente, y `{fecha}`, hasta cuándo vale) o con el texto de TienditApp. Sin plantilla aprobada en Meta solo llega a quien escribió en las últimas 24 horas.
  - La baja del cliente de la cotización no la detiene: él le pidió esa cotización al negocio. A otro número que pidió no recibir mensajes no se le manda: se rechaza al pedirlo. Esos mensajes no salen en Campañas › Mensajes ni en los mensajes fallidos de Inicio: se ven en su cotización.
  - Sin WhatsApp conectado, la pantalla sigue ofreciendo mandarla desde el teléfono (`wa.me`).
- **Límites**: cada versión se manda 5 veces al día por cada medio y un negocio 300 cotizaciones al día por cada medio, contadas bajo un candado de envíos del negocio, así que dos envíos a la vez no pasan del último.
- **El historial**: el detalle de cada versión muestra sus últimos 5 correos y 5 WhatsApp con dónde va cada uno (enviándose, enviado, entregado, leído, o no se envió y por qué), y vuelve a preguntar mientras alguno va en camino.
- **El rollback** se detiene (50052) en cuanto se mandó una cotización desde TienditApp o un negocio eligió una plantilla para ellas.
- **Al publicar o volver a la versión anterior**, el worker de la 29C aparta el correo de una cotización que tome (no conoce el evento) y se queda «Enviándose»; [deploy-azure.md](../development/deploy-azure.md) tiene la consulta que lo regresa a la cola.

### Etapa 2: la solicitud de la visita

10. **El negocio decide cómo cobra la visita** (decisión 5, `TenantSettings.HomeVisitChargePolicy`), con el servicio del catálogo que eligió como su visita («Visita y diagnóstico»):
    - **Se cobra:** ese servicio, más el cargo de la zona si la zona lo tiene.
    - **Sin costo:** ni el servicio ni el cargo de la zona.
    - **Se descuenta si contratas:** se cobra como la primera, y la cotización aceptada descuenta lo que se pagó de la visita (punto 14).

    El servidor calcula el costo de la visita con la política, el servicio y la zona del código postal. La tienda, la tarjeta y el aviso dicen lo mismo, y el cliente lo ve antes de pedirla.
11. **El cliente pide una hora exacta** (decisión 4). Escribe su código postal (una zona activa debe llegar a él), elige el día, una hora libre y, si el negocio elige quién agenda en línea ([ADR 0032](0032-online-booking-staff.md)), con quién.
    - Las horas salen de `BookingSlots` con la duración del servicio de la visita (una hora sin ella). Se suma el traslado del negocio (`HomeVisitTravelMinutes`) antes y después de cada visita, contra todas las citas de esa persona, como en la agenda ([ADR 0020](0020-home-visits.md)).
    - Escribe qué pasa (hasta 500 caracteres), su dirección y referencias (`DeliveryAddress`; una cuenta usa sus direcciones guardadas), su nombre, su teléfono y, si quiere, su correo para recibir la cotización.
    - Las fotos del problema quedan para después: necesitan una subida pública validada, privada por negocio y con límite por solicitud, y esta etapa no abre subidas anónimas.
12. **La solicitud es una cita «Por confirmar» con su visita** (`Appointment` en `Requested`, con `AppointmentVisit`). Usa la llave de la solicitud, el candado por negocio, el límite de 3 por teléfono, el enlace de seguimiento, Mis citas y el aviso por WhatsApp de la [ADR 0025](0025-online-booking.md).
    - En este modo no aplica `NoPremises`.
    - El cargo por visita sale de la política: «Sin costo» lo deja en cero.
13. **El negocio decide en la Agenda**, como con cualquier solicitud: la acepta, la rechaza o la mueve antes de aceptarla. Aceptada, entra a la Ruta del día. La visita se cobra una vez con «Cobrar», como hoy ([ADR 0020](0020-home-visits.md)), y solo ahí hace falta una caja.

### Etapa 3: la cotización

14. **`Quote`, un agregado nuevo** (`ITenantScoped`, con su filtro y su `rowversion`). Guarda:
    - el cliente, registrado o con su nombre y teléfono;
    - la cita de la visita, si viene de una;
    - un folio propio con `IFolioGenerator`;
    - sus renglones: servicios o productos del catálogo, o escritos a mano, con cantidad y precio en `decimal`;
    - la vigencia (15 días por omisión, por negocio) y una nota.

    El servidor calcula los impuestos y el total con la política del negocio, como en un pedido. El navegador nunca manda un total.
    - Con «Se descuenta si contratas» y una visita cobrada, la cotización lleva el renglón «Visita (se descuenta)» por lo que se pagó, calculado por el servidor desde la venta de la visita. Si esa venta se cancela, el renglón deja de aplicar.
15. **Sus estados los cambia el agregado:** Borrador → Enviada → Aceptada, Rechazada o Vencida. Una enviada no se edita: corregirla crea una versión nueva, y la anterior queda «Reemplazada» en el historial, con lo que decía. Vence sola cuando pasa su vigencia.
16. **Le llega al cliente por WhatsApp y por correo** (decisión 6), desde el outbox:
    - El correo sale de la plataforma con el nombre «{Negocio} vía TienditApp» y `Reply-To` al correo del negocio, como la factura ([ADR 0049](0049-cfdi-email.md)), con la cotización en el cuerpo y su enlace. Sin PDF, porque TienditApp no genera PDF hoy.
    - El WhatsApp lleva el folio, el total, la vigencia y el enlace. Fuera de las 24 horas necesita una plantilla aprobada en Meta, como el aviso «En camino».
    - El enlace es como el de un pedido ([ADR 0023](0023-order-tracking.md)): va en el fragmento, abre solo esa cotización sin datos del cliente fuera de su nombre, y deja de abrir 30 días después de vencer.
    - Cada envío queda en su historial con su resultado, como `CfdiEmail`.

    Implementado en la Fase 29D (arriba).
17. **El negocio registra si el cliente la aceptó o la rechazó**, con lo que le respondió por WhatsApp o por correo, y queda en la actividad. Aceptada, se convierte en el trabajo: una orden de servicio con sus renglones o las citas para hacerlo. La orden lleva sus fotos del antes y del después ([ADR 0024](0024-service-photos.md)).

### Después

- Que el cliente acepte la cotización desde su enlace, con un anticipo por Mercado Pago ([ADR 0031](0031-online-payments.md)).
- Fotos del problema en la solicitud.
- PDF de la cotización y documento adjunto por WhatsApp.
- El tiempo de respuesta del negocio como criterio de orden en Explorar.
- Que la búsqueda de Explorar encuentre los servicios de estos negocios: hoy busca solo los productos del menú ([ADR 0028](0028-product-search-and-favorites.md)).

## Consecuencias

- **Lo que logra:**
  - Un oficio a domicilio aparece en Explorar por lo que hace y en su categoría, sin abrir una caja. El cliente sabe si llega a su código postal y cuánto cuesta la visita antes de pedirla.
  - La visita se agenda en línea a una hora exacta y respeta el traslado entre una casa y otra.
  - La cotización queda guardada, con sus versiones, su vigencia y lo que el cliente decidió, y se vuelve el trabajo sin capturarlo dos veces.
  - Los negocios nuevos no publican la casa del dueño, y los registrados se enteran de que la publican.
- **Lo que limita:**
  - El cliente no acepta la cotización en línea: responde, y el negocio lo registra.
  - Sin plantilla aprobada en Meta, el WhatsApp de la cotización solo llega a quien escribió en las últimas 24 horas. El correo llega si el cliente lo dio.
  - «Cerca de mí» no ordena a estos negocios: no publican su punto, y las zonas son por código postal, no por distancia.
  - La búsqueda de Explorar no encuentra por su ciudad a uno «Solo a domicilio», porque no la muestra.
  - Una categoría más necesita migración, enum, cliente y la lista del registro ([ADR 0021](0021-business-directory.md)).
- **Decisión por omisión** (sin preferencia expresa del dueño). La Fase 29A aplicó la primera, la segunda y la cuarta, y la Fase 29C la vigencia de la cotización (15 días, que cada negocio cambia):
  - el interruptor empieza prendido para los giros de oficio y apagado para los demás;
  - Carpintería y Herrería y soldadura conservan el local al registrarse, y los demás oficios empiezan «Solo a domicilio»;
  - la cotización vence a los 15 días;
  - la categoría sigue al giro en los negocios que no la decidieron, como dice la [ADR 0021](0021-business-directory.md).
- **Reversibilidad:** cada etapa lleva su migración y su `rollback.sql`, que se detiene en cuanto hay datos que se perderían: solicitudes de visita, cotizaciones o negocios cuyo dueño eligió «Hogar y reparaciones».
