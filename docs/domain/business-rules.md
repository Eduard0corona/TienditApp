# Reglas de negocio

## Órdenes

- Una orden nunca refiere a la vez a `CustomerUserId` y `TemporaryCustomerId`. La orden de un cliente exige uno de los dos; una venta de mostrador creada por el negocio (`CreatedByBusiness`) a público en general puede no tener ninguno. `CK_Orders_CustomerLink` aplica la misma regla en la base de datos.
- Una orden inicia en `Pending` y su estado cambia mediante `TransitionTo` o `Cancel` con una máquina creada por tipo de negocio/orden.
- Solo se agregan items mientras la orden está `Pending`; cantidad debe ser positiva y precio no negativo.
- Las órdenes Service pueden tener `ServiceOrderDetail` y piezas; otros tipos no.
- Las colecciones de items, historial, pagos y piezas son de solo lectura.
- Un pago debe ser positivo, no puede exceder `Total` acumulado y no se admite en una orden cancelada. Una venta puede crearse con sus pagos (hasta 10); si uno es inválido, no se crea.
- El IVA de cada línea se fija al venderla (tasa del producto o del negocio) y la orden conserva si sus precios incluían IVA; cambiar la política después no altera ventas anteriores.
- Una venta de mostrador pagada se cierra sola si el negocio mantiene `AutoCloseCounterSales` (ver [ciclos de vida](order-lifecycles.md)).
- Una orden con dinero sin reembolsar no se puede cancelar: se reembolsa primero o se usa "Anular venta", que reembolsa y cancela en un paso. Cancelar cancela también sus piezas aceptadas o en progreso.
- Un reembolso no puede superar lo cobrado y no devuelto ni devolver más unidades de las vendidas; pagos y líneas no se editan (ver [finanzas](finance-payments-and-taxes.md#reembolsos-y-anulaciones-fase-2)).
- Cada orden guarda su canal (`Channel`): mostrador, WhatsApp (tomada desde un chat), en línea (reservado para el menú público) o, desde la Fase 22F, Rappi, Uber Eats y DiDi Food (ver [pedidos de plataformas](#pedidos-de-plataformas-de-reparto-fase-22f)). El canal no cambia después de crear la orden: los reportes por canal y las reglas de las plataformas dependen de eso.
- Una orden admite como máximo 200 líneas, 10,000 unidades por línea y 500 piezas de servicio, además del `MaxQuantityPerOrder` de cada producto.
- La creación enlaza la cuenta cuyo perfil tiene ese teléfono solo si esa cuenta lo confirmó por SMS (`PhoneNumberConfirmed` y su teléfono confirmado es el del perfil, `CustomerPhoneOwnershipRules`); si no, crea/usa cliente temporal. El teléfono es opcional solo en ventas de mostrador a público en general (sin cliente seleccionado o con el cliente "Publico en general"): la orden queda sin cliente, con nombre "Público en general" si no se captura otro, y no recibe avisos de WhatsApp. Un cliente con nombre y los pedidos del propio cliente siguen requiriéndolo. Un teléfono que no se puede normalizar a E.164 responde 400 con un mensaje en español (`PhoneNumberRules.InvalidPhoneMessage`) en lugar de un 500.
- Las órdenes de un cliente temporal se vinculan a una cuenta solo cuando su dueño confirma el teléfono (desde el perfil con `ConfirmPhoneChangeCommand`, o con `ConfirmPhoneCommand` mientras la cuenta no tiene nada confirmado), en la misma transacción que la confirmación; el registro de cliente no las vincula ni manda código por SMS: la cuenta se activa con el enlace del correo.
- Registrar un teléfono no lo aparta: si lo tiene un perfil no verificado, la cuenta nueva nace sin perfil. Al confirmar el teléfono, el perfil de quien lo confirmó toma el número (se crea o cambia de número) y el perfil no verificado lo pierde; su cuenta se conserva. Si otra cuenta ya lo confirmó, el registro se rechaza y una confirmación posterior no le quita el número.

## Pedidos de plataformas de reparto (Fase 22F)

Un pedido de Rappi, Uber Eats o DiDi Food entra por la app de la plataforma, y el negocio lo registra en Vender con el folio de la app ([ADR 0013](../adr/0013-delivery-platform-orders.md)). Las reglas viven en el agregado (`Order`, que responde 422 si alguien se las salta), en el validador de la venta (400, con sus mensajes) y en el caso de uso:

- **Quién lo registra:** el negocio (`CreatedByBusiness`). El menú en línea y el chat nunca crean uno.
- **Cliente:** no se liga a un cliente ni guarda teléfono, porque la app no los comparte. Solo lleva el nombre para la bolsa, y un teléfono o una mesa en blanco se guardan como nulos.
- **Mesa, cuenta y propina:** no lleva mesa, no se abre como cuenta y no lleva propina, porque la de la app es del repartidor.
- **Solo productos:** el tipo de pedido es `Retail`, sin `ServiceDetail` ni productos con unidad de servicio (hora, sesión o servicio).
- **Folio:** es obligatorio. Se normaliza (sin espacios ni un `#` inicial, en mayúsculas) y lleva de 1 a 30 letras sin acento, números o guiones.
- **Quién cobró:** la plataforma (por omisión) o el negocio. Si cobró la plataforma, la venta no trae pagos: el servidor registra el pago «Plataforma» por el total. Si cobra el negocio, se cobra por la caja, sin propina.
- **Plataforma activa:** la plataforma debe estar activa en el negocio al registrar el pedido. Si no, responde 400 `business_rule` con «Rappi no está activa en tu negocio: actívala en Configuración › Negocio › Plataformas de reparto.».
- **Con conexión:** se registra con conexión. Una venta capturada sin conexión (`CapturedOfflineAt`) se rechaza.
- **Existencias y horario:** las existencias van en «vender y avisar» aunque el negocio bloquee, y no se exige el horario del producto, porque la app ya aceptó el pedido. Un producto apagado sí se rechaza.
- **Precios:** desde la Fase 22F-2 se cobran los de la app de esa plataforma ([ADR 0014](../adr/0014-delivery-platform-prices.md)). Cada producto y cada opción sin precio propio en la app se cobra con el del menú, y cada línea copia el precio que cobró. Ver [precios en las apps](#precios-en-las-apps-de-las-plataformas-fase-22f-2).
- **Lo que fija el registro:** al registrarse, el pedido copia los términos de la plataforma, su revisión y la comisión, las retenciones y el neto esperado calculados (`PlatformOrderDetail`, ver [finanzas](finance-payments-and-taxes.md#pedidos-de-plataformas-de-reparto-fase-22f)). Después no se le agregan productos.
- **Pagos:** un pedido que cobró la plataforma no admite otros pagos. Lo que cobró se devuelve como «Plataforma», y nada más se devuelve así.
- **Folio repetido:** con el mismo folio de la misma plataforma ya registrado ese día del negocio (desde la medianoche en su zona horaria), en un pedido que no está cancelado ni reembolsado por completo, la venta responde 409 `possible_duplicate_order`: «Hoy ya registraste un pedido de esta plataforma con el mismo folio. Si es otro pedido, confírmalo para registrarlo.». Con `confirmPossibleDuplicate` se registra. Un reintento con la misma llave de idempotencia devuelve el pedido ya creado, sin preguntar. Es un aviso: dos capturas del mismo folio en el mismo instante pasan las dos.
- **Avisos y campañas:** nunca recibe avisos de WhatsApp, con cualquier política del negocio. Queda fuera de las campañas y de la ficha del cliente.
- **Bitácora:** `OrderCreated` lleva en todos los pedidos el canal, el folio de la app y quién cobró; en los demás canales, los dos últimos van vacíos.
- **Configuración por plataforma** (`DeliveryPlatformSetting`, Configuración › Negocio › Plataformas de reparto):
  - una fila por negocio y plataforma;
  - la cambian dueños y administradores, con la revisión que vieron (0 la primera vez); una revisión vieja responde 409 `concurrency_conflict`;
  - la comisión va de 0 % a 60 % y es obligatoria: el servidor exige los siete campos del guardado y responde 400 `validation_failed` si falta alguno, en lugar de tomarlo como 0;
  - el IVA de la comisión es de 16, 8 o 0 %;
  - el ISR retenido va de 0 % a 20 % y el IVA retenido de 0 % a 100 % del IVA del pedido, todos con hasta dos decimales.

  Cada cambio deja `DeliveryPlatformUpdated` en la bitácora, en el mismo guardado. Su metadata lleva el canal, los términos anteriores (vacíos la primera vez) y los nuevos, cada uno con el estado, las tasas, la base y la revisión. Una petición que no cambia nada no guarda ni registra, y desactivar no borra nada: los pedidos conservan su detalle.

## Precios en las apps de las plataformas (Fase 22F-2)

Cada negocio puede poner, en Catálogo › Precios en apps, lo que cobra en la app de cada plataforma ([ADR 0014](../adr/0014-delivery-platform-prices.md)). Las reglas viven en el agregado `Product`, cuyo rechazo el caso de uso devuelve como 400 `business_rule`; en el validador (400 `validation_failed`: el canal, de 1 a 500 productos, hasta 300 opciones por producto y nada repetido); y en el caso de uso:

- **Qué se guarda:** el precio de un producto en una plataforma (`ProductPlatformPrice`) y lo que suma una opción en una plataforma (`ProductOptionPlatformPrice`). Uno por producto u opción y plataforma, solo en Rappi, Uber Eats y DiDi Food.
- **Límites:** lo que suma una opción, los del menú: de −$100,000 a $100,000 (negativo para una variante más barata), con dos decimales como máximo. El precio de un producto, mayor a $0 y de hasta $1,000,000, con dos decimales como máximo: el tope y los decimales son propios de los precios en apps, porque el del menú solo pide mayor a 0.
- **Dónde se usan:** solo en los pedidos de esa plataforma. Mostrador, WhatsApp, el menú en línea, las mesas y el chat cobran el precio del menú.
- **Sin precio propio:** el producto o la opción se cobra en la app con el del menú. Guardar null quita el precio de la app.
- **Servicios:** no llevan precio en las apps, porque un pedido de plataforma lleva solo productos.
- **Oferta del menú:** el precio tachado (`CompareAtPrice`) es del menú; Vender no lo muestra en un producto con precio de la app.
- **Quién los cambia:** quien cambia los precios del menú (`ManageCatalog`), sin bitácora, igual que el menú. Cada pedido copia sus precios, así que cambiarlos nunca reescribe un pedido.
- **Guardado:** es todo o nada. Un producto ajeno o borrado, un servicio, una opción que ya no es del producto o un precio fuera de los límites rechaza el guardado completo con su mensaje. Las opciones que no se mandan no cambian. Un negocio sin el módulo de productos ni el de alimentos no guarda precios, y su lista sale vacía.
- **Al mismo tiempo:** si otra persona guarda el primer precio del mismo producto u opción, o borra el producto o la opción, entre la lectura y el guardado, la API responde 409 `concurrency_conflict` y no guarda nada. Dos cambios de un precio que ya existía no chocan: gana el último, como en los precios del menú. Cualquier otra falla de la base es un error, no un conflicto.
- **Opciones y productos:** una opción que se conserva en el editor de opciones conserva su precio en las apps. Una opción que se quita, o un producto que se borra, se lleva sus precios.
- **Total:** la venta sigue rechazando un `ExpectedTotal` distinto del que calcula el servidor. Si un precio de la app cambia entre que Vender carga el catálogo y registra el pedido, la venta se rechaza y Vender recarga.

## Liquidaciones de las plataformas (Fase 22G)

Cada depósito de Rappi, Uber Eats o DiDi Food se concilia en Reportes › Liquidaciones con los pedidos que paga y los ajustes que explican el resto ([ADR 0015](../adr/0015-delivery-platform-settlements.md)). Las reglas viven en el agregado `PlatformSettlement`, cuyo rechazo el caso de uso devuelve como 400 `business_rule`; en los validadores (400 `validation_failed`: la plataforma, las listas sin nulos ni repetidos y el motivo de la anulación); y en los casos de uso:

- **Neto esperado de un pedido:** la parte del neto esperado de su plataforma que corresponde a lo que el negocio conserva cobrado: R(`ExpectedSettlement` × (`PaidAmount` − `RefundedAmount`) / `OrderTotal`), redondeado una vez a centavos (`MidpointRounding.ToEven`). Si cobró el negocio, es la comisión que la plataforma descontará, en negativo; con todo devuelto, o sin cobrar todavía, es 0.
- **Pendiente:** un pedido de la plataforma con neto esperado distinto de 0 que no está en una liquidación abierta ni cerrada.
- **Qué lleva:**
  - hasta 2,000 pedidos que la persona elige entre los de su plataforma registrados hasta el final del periodo (la fecha del negocio) que no están en otra liquidación vigente. Uno que no trae nada (devuelto completo, o sin cobrar todavía) no entra; uno que ya estaba en la liquidación y se devolvió después se queda, en 0. La pantalla ofrece los pendientes: con más de 2,000, primero los del periodo y, en el lugar que quede, los más recientes de antes;
  - hasta 100 ajustes con motivo: promociones, faltantes y reclamos, cancelaciones, cargos de la plataforma, saldo anterior u «Otro», que pide descripción. El importe lleva signo (negativo cuando la plataforma descuenta), no es 0 y va de −$1,000,000 a $1,000,000 con dos decimales; la descripción, hasta 200 caracteres. Se leen en el orden en que se escribieron.
- **El depósito:**
  - el periodo, de hasta 31 días, sin terminar después de hoy ni empezar antes de 2000 (un año mal escrito);
  - la fecha del depósito, desde el inicio del periodo y sin llegar al futuro;
  - lo depositado, de −$10,000,000 a $10,000,000 con dos decimales: negativo si la plataforma dejó saldo en contra;
  - una referencia de hasta 60 caracteres y notas de hasta 500.
- **Una a la vez:** una liquidación abierta por plataforma (la segunda se rechaza con «Ya hay una liquidación abierta…») y un pedido en una sola liquidación vigente. Los dos los cuida además un índice único filtrado.
- **Las cuentas:** debió depositar = sus pedidos + sus ajustes; diferencia = depositado − debió depositar. El servidor calcula cada neto esperado al guardar.
- **Cerrar:** pide una diferencia de $0.00 y al menos un pedido o un ajuste. Antes de cerrar, vuelve a calcular el neto esperado de cada pedido. Si uno cambió desde el último guardado (una devolución en medio), guarda las cifras nuevas y responde 409 `conflict` para que la persona las revise. Una liquidación cerrada ya no cambia.
- **Anular:** una abierta o cerrada, con un motivo de hasta 500 caracteres. Sus pedidos vuelven a estar pendientes, y la liquidación se queda como historia: nada se borra.
- **Bitácora:** cerrar y anular dejan `PlatformSettlementClosed` y `PlatformSettlementVoided` en el mismo guardado, con la plataforma, el periodo, las cifras, cuántos pedidos lleva (no cuáles) y, al cerrar, sus ajustes; al anular, el motivo.
- **Al mismo tiempo:** guardar, cerrar y anular llevan la revisión que vio la persona; abrir una no lleva ninguna. Una revisión vieja, el mismo guardado enviado dos veces, o una liquidación abierta o un pedido tomado por otra persona en el mismo instante, responde 409 `concurrency_conflict` sin guardar nada. Un pedido que otra liquidación ya tenía al guardar responde 409 `conflict` con su motivo.
- **Quién:** la leen quienes leen los reportes (`ViewReports`); la abren, guardan, cierran y anulan dueños y administradores.
- **No es caja:** un depósito no es un movimiento de caja, y Ventas por canal, Inicio y el corte siguen en base caja. La liquidación redondea por pedido y Ventas por canal por fila, así que pueden diferir por centavos.

## Sucursal solo a domicilio (Fase 22A)

Una cocina sin local no publica su dirección ([ADR 0016](../adr/0016-delivery-only-branches.md)). Cada sucursal dice cómo atiende (`Branch.ServiceMode`): «Local abierto al público» o «Solo a domicilio, sin local».

- **Qué se publica:** con la sucursal principal solo a domicilio, el menú en línea no lleva su dirección (`AddressText.ForCustomers`) y avisa que es solo a domicilio, y la respuesta automática a «¿dónde están?» (también con un saludo, una pregunta de precios o el horario en el mismo mensaje) dice que no hay local para visitar, con la descripción del negocio y el menú si está prendido. «Mi dirección» o «mi ubicación» no preguntan dónde está el negocio. Ninguna respuesta automática lleva la dirección de una sucursal.
- **Quién la ve:** el equipo, en Configuración › Sucursales y en el almacén de la sucursal, que la copia al crearse.
- **Guardado:** dueños y administradores. Sin `serviceMode`, una nueva tiene local y una guardada conserva el que tenía; un valor desconocido es 400 `validation_failed` (`Elige cómo atiende la sucursal.`).
- **Giro:** el alta con «Comida a domicilio» crea la principal solo a domicilio. La guía de inicio, como a todo negocio de comida, ofrece la plantilla «Comida a domicilio». Cambiar el giro después no cambia ninguna sucursal.

## Zonas de entrega (Fase 22B)

A dónde entrega el negocio y cuánto cobra por llevar, por código postal ([ADR 0017](../adr/0017-delivery-zones.md)).

- **Cada zona** (`DeliveryZone`) lleva:
  - nombre, uno por zona en el negocio, sin distinguir mayúsculas;
  - de 1 a 300 códigos postales de cinco dígitos;
  - costo de envío, de $0 a $10,000;
  - pedido mínimo, de $0 a $100,000; $0 es sin mínimo;
  - «envío gratis desde», opcional; no aplica si el envío ya es gratis;
  - si está activa.

  Un negocio tiene hasta 30 zonas.
- **Un código postal, una zona:** no puede estar en dos zonas del mismo negocio, activas o no. Agregarlo a otra zona se rechaza diciendo cuál lo tiene, también cuando dos guardados lo agregan al mismo tiempo. Lo mismo con el nombre. Otro negocio puede tener el mismo nombre y el mismo código postal.
- **Montos:** el envío va como los precios del negocio: con IVA incluido si los precios lo incluyen (lo de omisión) y con el IVA sumado si no ([ADR 0018](../adr/0018-home-delivery-orders.md), que enmienda el 0017). El mínimo y el envío gratis se comparan con lo que paga el cliente en productos (con IVA), sin el envío.
- **Quién las cambia:** dueños y administradores, con la revisión que vieron. Un guardado sobre una revisión vieja, o de una zona que alguien borró, es 409 `concurrency_conflict`, en el orden en que la base escriba las filas. Guardar sin cambios no registra nada. Todo el equipo puede leerlas.
- **Bitácora:** `DeliveryZoneCreated`, `DeliveryZoneUpdated` y `DeliveryZoneDeleted` guardan lo que cobra la zona y cuántos códigos postales tiene, antes y después, nunca cuáles.
- **Dónde se usan:** en las respuestas automáticas de WhatsApp ([mensajería](messaging.md)) y en el pedido a domicilio (Fase 22C). Una zona desactivada no llega a ninguno de sus códigos postales.

## Pedido a domicilio (Fase 22C)

El pedido sabe a dónde va y cobra el envío de su zona ([ADR 0018](../adr/0018-home-delivery-orders.md)).

- **Qué guarda** (`OrderDelivery`, uno por pedido): código postal, dirección (hasta 300 caracteres), referencias (hasta 500, opcionales), el nombre de la zona que llegó, el envío cobrado ($0 si fue gratis) y su tasa de IVA, y el viaje del repartidor cuando se anota.
- **Cuándo:** solo al crear un pedido de productos, desde Vender o el menú en línea, antes de cualquier pago o estado. Nunca un pedido de Rappi, Uber Eats o DiDi (su repartidor lo lleva), una cuenta de mesa, un servicio, uno con mesa o uno capturado sin conexión. Después no toma más productos ni se vuelve cuenta.
- **La zona:** la activa del negocio que llega al código postal. Si ninguna llega: «Por ahora no llegamos al 72999.». Si los productos no alcanzan su mínimo: «El pedido mínimo para el 72000 es de $150.00.». El envío es el de la zona, o $0 desde su «envío gratis desde», y queda fijo en el pedido.
- **El envío en el total:** un concepto más, con la tasa por omisión del negocio y la política de IVA del pedido. Entra al desglose de IVA, al total, a lo que se cobra y al borrador de CFDI como «Envío a domicilio» (78102203, E48, con un aviso para confirmarlo con el contador). Con precios sin IVA, el envío se dice «más IVA» en las respuestas automáticas, el menú en línea y Vender.
- **Cierre:** un pedido a domicilio pagado no se cierra solo como una venta de mostrador: alguien lo marca entregado.
- **Avisos por WhatsApp:** un pedido a domicilio tomado en Vender avisa al cliente como uno del menú en línea, no como una venta de mostrador: el cliente lo espera en casa.
- **Menú en línea:** ofrece «A domicilio» mientras toma pedidos y hay alguna zona activa; el cliente cotiza su código postal sin ver el nombre de la zona. En una mesa no se ofrece. Una cocina sin local con zonas exige la dirección, salvo desde una mesa activa del negocio (una mesa inventada en el enlace no cuenta); sin zonas, sus pedidos llegan como antes.
- **Paquetenvia:** quien toma los pedidos anota o corrige el folio del viaje y su enlace (opcional, escrito con `https://`) en Pedidos, no en un pedido cancelado. Anotar el mismo viaje otra vez no registra nada.
- **Bitácora:** `OrderCreated` dice si va a domicilio y cuánto cobró de envío, nunca la dirección; `OrderCourierShipmentRegistered` guarda repartidor, folio anterior y nuevo y si hay enlace, nunca el enlace.

## Salida a entregar y cobro contra entrega (Fase 22D)

El pedido sale con Paquetenvia, que puede cobrar en efectivo al cliente, y lo que entrega se liquida en Caja ([ADR 0019](../adr/0019-courier-cash-on-delivery.md)).

- **«Paga con»:** el billete con que paga el cliente, de $0.01 a $100,000 con dos decimales; lo de más es el cambio. Al tomar el pedido (Vender o el menú en línea) es opcional y cubre el total; al salir, cubre lo que cobra el repartidor: «Con $200.00 no alcanza: hay que cobrar $275.00.».
- **Salió a entregar:** quien toma los pedidos anota el viaje y si el repartidor cobra en efectivo. Sale un pedido confirmado y hasta que se entrega, no uno pendiente («Confirma el pedido antes de mandarlo.») ni cancelado ni entregado. No es un estado del pedido: la cocina y los estados siguen igual.
  - Si cobra, cobra el saldo del pedido en ese momento; un pedido pagado no cobra nada («El pedido ya está pagado: el repartidor no cobra nada.»). Si no cobra, no hay «paga con».
  - Se corrige mientras va en camino; la primera salida queda como la hora en que salió. Guardar lo mismo no registra nada.
  - La bitácora `OrderDispatched` guarda repartidor, folio, si hay enlace, lo que cobra, con cuánto paga y si fue corrección; nunca el enlace ni la dirección.
- **«En camino»:** la primera salida avisa al cliente por WhatsApp, con el enlace si lo hay. No avisa una corrección ni un pedido que se entregó o canceló antes.
- **Lo que debe el repartidor:** el saldo de los pedidos que entregó cobrando y que nadie liquidó ni cobró. Si el cliente pagó una parte por otro medio, debe el resto. Lo que va en camino todavía no se debe; ahí cobra lo que salió a cobrar o, si se pagó una parte mientras tanto, el saldo («Ya está pagado: el repartidor no cobra nada.» si se pagó todo).
- **Liquidación** (Caja › Repartidores):
  - Se eligen los pedidos que entrega (hasta 200), lo que entregó y cómo (efectivo o transferencia), y los ajustes (hasta 50): viaje, comisión por cobrar y faltante se descuentan; «Otro» descuenta o suma y necesita descripción (hasta 200 caracteres).
  - Lo entregado debe ser los pedidos más los ajustes, al centavo; si no: «Entregó $500.00 y los pedidos con sus ajustes dan $505.00: la diferencia debe ser $0.00. Registra lo que falta como un ajuste.».
  - Cada pedido se cobra con su saldo en la caja propia y abierta de quien recibe; cada ajuste entra a esa caja como gasto (descuento) o depósito (suma) con el mismo método.
  - Un pedido se liquida una vez, con el saldo que se vio: si alguien lo cobró, lo liquidó o pagó una parte mientras tanto: «Un pedido de la lista ya no se le debe al repartidor (alguien lo cobró, lo liquidó o cambió): lee otra vez la lista.». Una liquidación no se anula ni se edita: se corrige con un reembolso o un movimiento de caja.
  - La bitácora `CourierSettlementRegistered` guarda repartidor, método, montos, pedidos y ajustes, sin direcciones.

## Servicios a domicilio (Fase 22E)

Una cita puede ser en la casa del cliente, con el cargo por visita de su zona, y se cobra con su venta ([ADR 0020](../adr/0020-home-visits.md)).

- **Dónde:** «En el negocio» o «A domicilio» con el código postal (cinco dígitos), la dirección (hasta 300 caracteres: «Escribe a dónde es la visita: calle, número y colonia.») y referencias opcionales (hasta 500).
- **La zona:** la de entrega activa que llega al código postal; su costo de envío es el cargo por visita (sin pedido mínimo ni envío gratis). Sin zonas activas: «Para citas a domicilio, primero da de alta tus zonas en Configuración › Zonas de entrega.»; fuera de ellas: «Por ahora no hacemos visitas en el 72000.».
  - La misma dirección conserva la zona y el cargo acordados aunque la zona cambie después; otra dirección se cotiza otra vez. Quitar «A domicilio» quita el cargo. Solo en una cita abierta.
  - La bitácora (`AppointmentVisitArranged`, `AppointmentVisitRemoved`) guarda la zona y el cargo, nunca la dirección.
- **Traslado:** entre una visita y otra cita de la misma persona pasan al menos los minutos del negocio (de 0 a 240, 30 por omisión): «Luis necesita 30 minutos de traslado entre una visita a domicilio y su otra cita.». Una visita lo pide contra cualquier cita de esa persona; una cita en el negocio, solo contra sus visitas. Se revisa después del traslape; sin persona asignada o con 0 no se revisa. Cambiarlo no mueve las citas ya agendadas.
- **Ruta del día:** las visitas de un día por persona y en orden de hora, sin canceladas ni «no se presentó»; las de nadie, al final.
- **Cobrar la cita:** su venta es de mostrador del negocio («La venta de una cita la registra el negocio en el mostrador: sin plataforma, mesa, cuenta abierta ni envío.») y con conexión («La venta de una cita se registra con conexión: la cita se revisa al cobrar.»).
  - La cita es del negocio («La cita no existe.»), no está cancelada («La cita se canceló: ya no se cobra.») ni «no se presentó» («El cliente no se presentó a la cita: no se cobra.»), y no tiene ya su venta («La cita ya tiene su venta.»), salvo que esa venta se haya cancelado.
  - Con la venta, la cita queda completada y guarda qué venta la cobró. Dos cobros a la vez: el segundo recibe 409.
  - Una visita con cargo lo suma como concepto propio: un monto más del desglose con la tasa del negocio al cobrar y la política de IVA del pedido, como el envío. Solo en una venta nueva, con productos y antes de cobrar; nunca con envío, plataforma o cuenta de mesa.
  - El borrador de CFDI lleva el concepto «Cargo por visita a domicilio» con la clave del SAT del primer servicio de la venta (o 01010101), la unidad E48 y el aviso de confirmarla con el contador.
  - `OrderCreated` guarda el cargo por visita.

## Cancelación y trazabilidad

El agregado conserva `CancellationReason`, agrega historial con estado anterior/nuevo/usuario y emite evento con estado anterior/motivo. Sin embargo, el motivo es opcional y `OrderStatusHistory` no guarda el motivo; es una brecha frente a la regla aprobada de conservar motivo, estado anterior, usuario y trazabilidad completa. Toda cancelación futura debe exigir/normalizar la evidencia necesaria sin reescribir historial.

## Servicio y citas

- Una pieza inicia `Accepted`; pasa a `InProgress`, luego `Completed`, o puede cancelarse desde los dos primeros estados.
- Una pieza completada/cancelada no puede asignarse.
- El caso de uso sincroniza estado de la orden desde las piezas trackeables. Los cambios de pieza pasan por `Order.ChangeServicePieceStatus` para que el rowversion de la orden detecte cambios simultáneos.
- Completar una pieza consume su receta en el mismo guardado; si falta stock y el negocio bloquea (por defecto), la pieza no se completa y el mensaje nombra el insumo; con "vender y avisar" se completa y el insumo queda en negativo.
- Las citas tienen su propio enum y método de cambio de estado; deben mantener reglas en el agregado.
- Una cita puede ligarse a un servicio del catálogo y a una persona activa del equipo del mismo negocio (sus nombres se toman de ahí); una misma persona no tiene dos citas que se traslapen. Una cita cerrada (completada, cancelada o no presentada) ya no se edita ni se reprograma; reprogramar avisa al cliente por WhatsApp. La agenda abre con las citas de hoy, la más próxima primero; el historial se lee de la más reciente hacia atrás.

## Inventario, compras y caja

- Existencia disponible es `OnHandQuantity - ReservedQuantity`; una salida normal no permite negativo. El consumo de una venta sigue la política de existencias del negocio (bloquear o vender y avisar, ver [inventario](inventory-and-purchasing.md)).
- Archivar una categoría la saca de Vender, del menú en línea y de los formularios; si tiene productos, primero se pasan a otra categoría activa del mismo negocio. Restaurarla la devuelve tal como estaba.
- Reservado no puede ser negativo ni superar existencia.
- Los movimientos tienen líneas positivas y el tipo define el signo aplicado.
- Una compra no repite insumo+ubicación, no recibe más de lo pendiente y distingue recepción parcial/total; un borrador se envía, y solo una compra sin nada recibido se cancela.
- Una caja cerrada no recibe movimientos y no puede cerrarse dos veces; el monto contado al cerrar no puede ser negativo.
- El tipo de movimiento de caja define su signo; los importes capturados son positivos. Solo el efectivo forma parte del saldo esperado.
- Cada cajero tiene como máximo una caja abierta por negocio; solo él o un administrador la cierra o le registra gastos y retiros. Un cierre con diferencia exige notas.
- Todo cobro que recibe el negocio, y todo reembolso de ese dinero, entra a una caja abierta, sea cual sea el método: la del cajero o, si no tiene, la única abierta de la sucursal de la venta. Sin una que lo tome se rechaza; guardar un pedido sin cobrar no la necesita. El menú en línea solo recibe pedidos mientras haya una caja abierta en la sucursal principal, adonde llegan. La excepción es el pago «Plataforma» (Fase 22F, [ADR 0013](../adr/0013-delivery-platform-orders.md)): lo cobró Rappi, Uber Eats o DiDi Food en su app, así que no entra a ninguna caja ni exige una abierta, y su reembolso tampoco.
- El horario del negocio (`OpeningHours`) tiene hasta dos horarios por día. Un cierre igual o anterior a la apertura termina al día siguiente, y la misma hora en los dos es abierto las 24 horas. Solo el último horario de un día pasa de la medianoche, sin terminar después de que abre el día siguiente. Con horario, el menú en línea no recibe pedidos para recoger fuera de él; los de una mesa del negocio en uso, sí ([ADR 0011](../adr/0011-opening-hours.md)).
- Los días especiales (Fase 21, [ADR 0012](../adr/0012-special-days.md)) cierran el negocio en una fecha o le dan otro horario (uno o dos, con las reglas de un día de la semana), con un motivo opcional que ven sus clientes: máximo 40 caracteres, en una sola línea. Se guarda una fila por fecha (`SpecialDay`), y las de un mismo periodo comparten la regla.
  - Un periodo dura de 1 a 92 días seguidos, empieza hoy o después en la hora del negocio y termina como mucho a 366 días. Un negocio tiene como máximo 120 fechas próximas (las pasadas no cuentan), y una fecha está en un solo periodo, también si dos personas guardan a la vez (índice único `(TenantId, Date)`).
  - Un día especial reemplaza los horarios que empiezan en su fecha, por encima del horario de la semana; el de la noche anterior sigue pasando de la medianoche. Aplica también sin horario de la semana: entonces los demás días cuentan como abiertos todo el día, y eso nunca se dice como horario.
  - Las fechas pasadas no cambian. Un periodo en curso conserva su primer día, puede terminar desde ayer, y un cambio de regla pasa los días desde hoy a un periodo nuevo; quitarlo lo acorta a ayer. Un periodo pasado no se edita ni se quita. Cada cambio queda en la bitácora con el periodo antes y después, y una petición que no cambia nada no guarda ni registra.
  - En un día especial cerrado, el menú en línea no recibe pedidos, ni siquiera desde el QR de una mesa en uso (decisión por omisión, `PublicOrderRules.TableOverridesClosure`), tampoco pasada la medianoche mientras seguiría la noche que quitó. Al terminar un horario especial, la mesa sí pide, como fuera de horario.
- Solo las entradas modifican el costo promedio de un insumo.

## Mensajería

- Quien escribe BAJA (o STOP) deja de recibir campañas y recordatorios; con ALTA vuelve. La baja no se borra al volver: se conserva cuándo y cómo se eligió. Los avisos de sus pedidos y de sus citas siguen llegando.
- Las bajas se revisan al momento de enviar cada mensaje, no solo al elegir destinatarios.
- Un recordatorio no se envía si la cita cambió de hora, se canceló, se cerró o ya empezó.
- Agendar, reprogramar, confirmar o cancelar una cita encola un aviso al cliente con la cita completa, en el mismo guardado; si llega uno más reciente de la misma cita antes de enviarse, el anterior se omite. Desde la Fase 22E, cambiar el código postal de una visita también avisa, y el aviso dice «a domicilio (CP 72000)» y el cargo por visita, nunca la calle.
- Una campaña llega como máximo a 5,000 clientes y no se envía dos veces al mismo número.

## Guardrails

- No cambies estados por asignación directa.
- No calcules estados/totales/impuestos/saldos/existencias finales en frontend.
- Cambios que cruzan orden, inventario, caja, auditoría o notificación pasan por casos de uso coordinados.
- Mantén invariantes en agregados y formato/entrada en FluentValidation/Application.
- Toda nueva regla necesita pruebas positivas y negativas.
