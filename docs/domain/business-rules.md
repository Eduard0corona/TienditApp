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

### Importar el estado de cuenta (Fase 22H)

En una liquidación abierta, o en una nueva, la persona importa el estado de cuenta de la plataforma en CSV y la pantalla propone qué pedidos lleva y qué ajustes explican el resto ([ADR 0040](../adr/0040-platform-statement-import.md)). La comparación (`MatchPlatformStatementQuery`) no guarda nada; lo propuesto se guarda con la liquidación, que vuelve a validar todo con las reglas de arriba.

- **El archivo:** se lee en el navegador, hasta 5 MB, en UTF-8, UTF-16 con su marca o Windows-1252, separado por comas, punto y coma o tabuladores; un libro de Excel o un PDF se rechazan. El encabezado es el primer renglón con dos columnas o más. La persona elige las columnas de folio, importe y concepto (opcional); la pantalla las propone por el encabezado y recuerda por plataforma, en ese dispositivo, las que se usaron.
- **Qué renglones se mandan:** hasta 5,000, con su línea del archivo. No se mandan los renglones vacíos, los de totales del archivo («Total», «Total:», «TOTAL A PAGAR», «Gran total», «Subtotal»…, sin folio o con esa palabra en el folio), ni los que tienen un importe que no se lee, con más de dos decimales o fuera de ±$10,000,000, o un folio de más de 60 caracteres; la pantalla dice cuáles y por qué. El separador decimal de los importes sale de su columna.
- **Validación (400 `validation_failed`):** una plataforma de reparto; el periodo, con las mismas reglas que el de la liquidación, sin terminar después de hoy (del negocio); de 1 a 5,000 renglones sin nulos ni líneas repetidas, cada uno con su línea (desde 1), un importe de dos decimales hasta ±$10,000,000, un folio de hasta 60 caracteres y un concepto de hasta 200. Pasado el límite de renglones, un solo mensaje; la petición admite hasta 8 MB.
- **La liquidación:** una de otro negocio, o que no existe, responde 404; una de otra plataforma o que no está abierta, 400. Sin liquidación (una nueva), otra abierta de la plataforma responde 400 con «Ya hay una liquidación abierta…».
- **Cómo se agrupan:**
  - cada folio se normaliza como lo guardan los pedidos (`PlatformOrderNumber`), y sus renglones se suman: el pedido y lo que se le descuenta en otra línea;
  - un renglón sin folio, o con uno sin letras ni números («-»), va con los de su mismo concepto, sin distinguir mayúsculas ni espacios de más;
  - un folio que no puede ser de la plataforma («R/9») va aparte, como ilegible.
- **Qué es cada folio:** entre los pedidos de la plataforma con ese folio registrados hasta el final del periodo (la fecha del negocio), la liquidación puede tomar los que ya tiene (también en 0) y los que no están en otra liquidación vigente y traen un neto esperado distinto de 0. Uno solo que ya tiene, o uno solo en total, hace el folio **ya en esta liquidación** o **pendiente**, con su neto esperado y la diferencia contra el estado de cuenta; si no, con varios, **varios pedidos** (lista hasta 10) y la persona elige. Sin ninguno, el folio dice por qué, en este orden: **después del periodo** (uno que traería algo, también de la liquidación si se acortó el periodo), **en otra liquidación vigente** (el más reciente, con cuál), **sin nada que esperar** (el más reciente: devuelto completo o sin cobrar todavía); sin pedidos, **ninguno**. De los folios que la liquidación no puede tomar se lee un solo pedido, no su historial.
- **Totales:** lo que suma el estado de cuenta = lo de los folios que entran + los conceptos + lo que no entra (los demás folios, incluidos los de varios pedidos, y los ilegibles). Responde también el neto esperado de los folios que entran.
- **Lo que propone la pantalla:**
  - incluye los pedidos de los folios que entran y quita los incluidos que el estado de cuenta no trae; de un folio con varios no se incluye ninguno solo: se queda el que la persona marcó o guardó;
  - ajustes con la descripción «Estado de cuenta: …»: la diferencia de los pedidos que entran; los folios que no entran y los ilegibles (sin los de varios pedidos); y uno por concepto, con el motivo que sugieren sus palabras. Sin pasar de 100 ajustes: los conceptos que no caben van juntos;
  - importar otra vez reemplaza los ajustes con esa descripción y deja los escritos a mano;
  - lo depositado, solo si estaba vacío y el archivo cuadra (sin renglones con importe fuera y con su total, si lo trae, igual a lo que se tomó), y una nota con el archivo y cuántos renglones tenía, si cabe en las 500 letras.
- **Quién:** dueños y administradores (`[RequiresTenantManager]`), los mismos que guardan la liquidación.
- **Por ID largo (Fase 22I):** cada renglón es un folio o el ID largo del pedido, de hasta 100 caracteres. Se normaliza como folio, o como ID largo cuando es demasiado largo para folio, y se buscan los pedidos cuyo folio o ID largo coincide ([ADR 0041](../adr/0041-platform-order-ids.md)). Los renglones que nombran el mismo pedido por su folio y por su ID largo se suman en uno, bajo el primero. Un folio de varios pedidos sigue para elegir aunque uno de ellos ya lo tome otro renglón por su ID largo. De un ID largo que la liquidación no puede tomar se leen sus pedidos completos (son uno o los pocos capturados dos veces) para decir por qué.

### El ID largo del pedido (Fase 22I)

Un pedido de Rappi, Uber Eats o DiDi Food puede guardar, además del folio corto, el ID largo que le da la plataforma: el UUID de Uber Eats, el ID de DiDi o el número de orden de Rappi. Sus estados de cuenta nombran los pedidos por él ([ADR 0041](../adr/0041-platform-order-ids.md)).

- **El valor (`PlatformOrderId`):** se normaliza como el folio (sin espacios alrededor ni dentro, sin un `#` inicial, en mayúsculas). Son letras de la A a la Z, dígitos y guiones, de 6 a 64; se leen hasta 100 caracteres antes de normalizar. Es opcional y no cambia ningún importe.
- **Al capturar:** `externalOrderId` en la venta, solo en un pedido de plataforma; en otro pedido es un 400 con «El folio de la app, su ID largo y quién cobró solo van en pedidos de Rappi, Uber Eats o DiDi Food.».
- **Después:** `PUT /api/v1/orders/{id}/platform-order-id` lo escribe, lo corrige o lo quita (vacío) en cualquier estado del pedido, para quien toma los pedidos (`ManageOrders`). Un pedido que no es de plataforma responde 400; uno de otro negocio, 404. El mismo ID otra vez no cambia nada. El cambio toca el pedido: dos correcciones a la vez sobre la misma versión, la segunda responde 409 `concurrency_conflict`.
- **Repetido:** otro pedido de la misma plataforma del negocio con el mismo ID largo, de cualquier fecha, que no está cancelado ni devuelto completo responde 409 `possible_duplicate_order`. Al capturar se confirma con `confirmPossibleDuplicate` y la misma llave; al corregir, con `confirmPossibleDuplicate`. Si al capturar se repiten el ID largo y el folio de hoy, un solo aviso nombra los dos, porque una confirmación cubre ambos. No hay índice único. El mismo ID en otro negocio no cuenta.
- **Bitácora:** `OrderCreated` incluye el ID largo con el que se capturó, y cada cambio deja `OrderPlatformIdChanged` con la plataforma, el folio, el anterior y el nuevo.
- **Búsqueda:** Pedidos y Reportes › pedidos de plataformas encuentran el pedido por una parte del ID largo, escrita como se guarda.
- **Dónde se ve:** el detalle y la lista de Pedidos, Reportes y la liquidación. La cocina y el ticket siguen con el folio corto.

### Las retenciones de cada plataforma (Fase 22J)

Rappi, Uber Eats y DiDi Food le emiten al negocio un CFDI de retenciones 2.0 (clave 26) cada mes o cada semana con el ISR y el IVA que le retuvieron. En Reportes › Liquidaciones › Retenciones se compara con lo que TienditApp calculó para sus pedidos ([ADR 0042](../adr/0042-platform-withholding-certificates.md)).

- **Qué se guarda:** lo que dice el XML (folio fiscal, emisor, RFC del receptor, periodo, fecha de emisión, total de operaciones, ISR e IVA retenidos con sus bases y total retenido), nunca el archivo. La plataforma la elige la persona.
- **Qué se lee:** solo la versión 2.0 con la clave 26, a un receptor nacional y timbrada, hasta 512 KB, sin DTD ni referencias externas. Una factura, un CFDI 1.0, otra clave, un PDF o un dato que falta se rechazan con un mensaje que dice cuál. No se valida el sello ni se consulta al SAT.
- **Quién:** suben y quitan dueños y administradores; la conciliación la ve quien lee los reportes (`ViewReports`).
- **Una vez por negocio:** un folio fiscal vigente responde 409 `conflict` con «Este CFDI ya está registrado en {plataforma}, {periodo}.». En otro negocio es otro registro.
- **RFC:** si el negocio escribió su RFC en Configuración y el CFDI va a otro, se rechaza sin nombrar ninguno. Sin RFC escrito, se acepta.
- **Periodo:** de enero a diciembre, el inicial no pasa del final, desde 2019 y sin empezar después del mes del negocio (uno semanal llega antes de que acabe el mes).
- **Importes:** de $0 a $1,000,000,000, con dos decimales: cada impuesto se suma como viene y se redondea una vez, alejándose de cero. Una línea de más de $1,000,000,000 se rechaza antes de sumarla. El total retenido no es menor que el ISR más el IVA salvo por 2 centavos de redondeo; el IEPS cuenta solo ahí.
- **Sustitución:** un CFDI que dice sustituir a otro (`CfdiRetenRelacionados`) quita al sustituido vigente en el mismo guardado. Uno que llega después del que lo sustituye (vigente o quitado) se guarda ya quitado, con «Lo sustituye el CFDI con folio …», y quita al que él sustituía: una cadena (A por B, B por C) subida en cualquier orden deja solo a C. Si ese CFDI ya estaba guardado, responde 409 `conflict` («Este CFDI ya se sustituyó por otro que está registrado en …: no cuenta.»). Un CFDI de otro negocio nunca quita uno de este.
- **Quitar:** con motivo (hasta 500 caracteres). Deja de contar, se queda en la lista como quitado y ese folio puede subirse otra vez. Uno ya quitado responde 409 `concurrency_conflict`.
- **La conciliación:** por plataforma y año, mes por mes en la zona del negocio. Lo calculado son los pedidos que cobró la plataforma, registrados en el mes, con su ISR, su IVA retenido y sus ventas sin IVA en proporción a lo que sigue pagado, redondeados por pedido. Los CFDI vigentes cuyos periodos se traslapan (una semana de DiDi que cruza el cambio de mes, con las semanas de los dos meses) se comparan juntos: cuentan completos en el último mes que cubren entre todos, y ese mes compara contra lo calculado en todos esos meses. Los meses anteriores que cubren dicen en qué mes se comparan, sin diferencia propia: ningún mes se compara dos veces. La diferencia es CFDI menos calculado (positiva: retuvo de más); la del año, solo de los meses con CFDI. Cada CFDI dice su tasa (retenido entre base), la del IVA solo cuando la base es el IVA.

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
  - El mismo lugar (código postal y dirección) conserva la zona y el cargo acordados aunque la zona cambie o deje de llegar después; cambiar solo las referencias las corrige sin cotizar. Otra dirección se cotiza otra vez. Quitar «A domicilio» quita el cargo. Solo en una cita abierta.
  - La bitácora (`AppointmentVisitArranged`, `AppointmentVisitRemoved`) guarda la zona y el cargo, nunca la dirección.
- **Traslado:** entre una visita y otra cita de la misma persona pasan al menos los minutos del negocio (de 0 a 240, 30 por omisión): «Luis necesita 30 minutos de traslado entre una visita a domicilio y su otra cita.». Una visita lo pide contra cualquier cita de esa persona; una cita en el negocio, solo contra sus visitas. Se revisa después del traslape; sin persona asignada o con 0 no se revisa. Cambiarlo no mueve las citas ya agendadas.
- **Ruta del día:** las visitas de un día (entre los años 2000 y 2100: «Elige el día de la ruta.») por persona y en orden de hora, sin canceladas ni «no se presentó»; las de nadie, al final.
- **Cobrar la cita:** su venta es de mostrador del negocio («La venta de una cita la registra el negocio en el mostrador: sin plataforma, mesa, cuenta abierta ni envío.») y con conexión («La venta de una cita se registra con conexión: la cita se revisa al cobrar.»).
  - La cita es del negocio («La cita no existe.»), no está cancelada («La cita se canceló: ya no se cobra.») ni «no se presentó» («El cliente no se presentó a la cita: no se cobra.»), y no tiene ya su venta («La cita ya tiene su venta.»), salvo que esa venta se haya cancelado.
  - La cobra quien vende y también agenda (`ManageAppointments`).
  - Con la venta, la cita queda completada y guarda qué venta la cobró (pagada o no: Pedidos la tiene); la agenda dice «Con venta», o «Venta cancelada» y vuelve a ofrecer «Cobrar». Dos cobros a la vez: el segundo recibe 409.
  - Una visita con cargo lo suma como concepto propio: un monto más del desglose con la tasa del negocio al cobrar y la política de IVA del pedido, como el envío. Solo en una venta nueva, con productos y antes de cobrar; nunca con envío, plataforma o cuenta de mesa.
  - El borrador de CFDI lleva el concepto «Cargo por visita a domicilio» con la clave del SAT del primer servicio de la venta (o 01010101), la unidad E48 y el aviso de confirmarla con el contador.
  - `OrderCreated` guarda el cargo por visita.

## Explorar (Fase 24A)

Los clientes encuentran los negocios por categoría y compran en el menú de cada uno ([ADR 0021](../adr/0021-business-directory.md)).

- **Quién aparece:** un negocio activo, con el menú en línea publicado (Solo menú o Menú con pedidos), con «Aparecer en Explorar» encendido (empieza así) y con al menos un producto que su menú muestra: disponible, que no sea un servicio y en una categoría activa del negocio.
- **Si recibe pedidos ahora:** la misma regla del menú y de su pedido (`OnlineOrderAvailability`). El menú tiene pedidos, el negocio está abierto por su horario y sus días especiales, y hay una caja abierta en su sucursal principal. La tarjeta dice «Pide en línea», «Pedidos en línea en pausa» (abierto sin caja), «Cerrado · abre …» o «Solo menú». Explorar no tiene mesa: un negocio cerrado sale cerrado. El menú y su pedido responden al momento; Explorar, sobre una foto de hasta 30 segundos.
- **Qué dice de cada negocio:** nombre, logo, categoría, descripción, ciudad de la sucursal principal (nunca la calle), si entrega a domicilio y si está cerrado. Nunca lleva el contacto ni los datos fiscales, y una cocina que solo entrega dice «Solo a domicilio» en lugar de su ciudad.
- **Orden:** primero los que reciben pedidos, luego los abiertos y al final los cerrados, cada grupo por nombre. Se busca por palabras del nombre, la descripción o la ciudad (nunca la de una cocina que solo entrega), sin acentos ni mayúsculas, hasta 60 letras («Busca con 60 letras como máximo.»), y se pagina de 24 en 24 (hasta 48).
- **Categorías:** once fijas (Comida, Abarrotes, Farmacia y salud, Ropa y accesorios, Electrónica, Ferretería y hogar, Papelería, Belleza y bienestar, Autos, Servicios, Otros). Cada negocio tiene una, que el dueño elige. Empieza con la de su giro, comparado sin acentos ni mayúsculas, o la de su tipo: comida va a Comida, un servicio a Servicios y una tienda a Otros. Sigue al giro mientras el dueño no la decida: cuando elige otra o guarda su tarjeta, aunque la deje igual, un giro nuevo solo la sugiere.
- **Lo decide el dueño** (o un admin) en Configuración › Negocio › «Cómo te ven en TienditApp»:
  - Elige la categoría y escribe una descripción de un renglón (sin saltos ni separadores de línea), de hasta 160 caracteres. Al guardar se recorta, y vacía se borra. Los mensajes de error son «La descripción admite como máximo 160 caracteres.», «La descripción va en un solo renglón.» y «La categoría no existe.».
  - Enciende o apaga «Aparecer en Explorar». Ocultarse no apaga el menú: el enlace y el QR siguen abriéndolo.
  - La tarjeta dice si aparece y, si no, por qué: ocultó el negocio, no ha publicado el menú, el menú no muestra productos o el negocio no está activo.
  - Cada cambio queda en la bitácora (`DirectoryProfileUpdated`) con la categoría y «Aparecer» de antes y de después, y si la descripción cambió, sin el texto.
- **Quién entra por `/`:** los visitantes y los clientes llegan a Explorar. El equipo de un negocio entra a su trabajo, y desde Explorar regresa con «Mi negocio».

## Comprar con tu cuenta (Fase 24B)

Una cuenta de cliente compra sin escribir sus datos cada vez ([ADR 0022](../adr/0022-shopping-with-an-account.md)).

- **De quién es el pedido:** un pedido del menú en línea enviado con la sesión de una cuenta de cliente activa queda en esa cuenta, sin confirmar el teléfono, y nadie lo reclama después por el teléfono. Sin sesión, o con la del equipo de un negocio, el teléfono decide, como antes. El recibo dice «Lo encuentras en tus pedidos.» y ofrece «Ver mis pedidos»; a un visitante le ofrece crear su cuenta.
- **Datos escritos:** la tienda escribe el nombre y el teléfono de la cuenta (sin el +52), y se pueden cambiar en cada pedido.
- **Direcciones de la cuenta:**
  - Hasta 10 por cuenta, para cualquier negocio que entregue. Cada una tiene nombre (hasta 60 caracteres, en un renglón; «Dirección» si no se escribe), código postal, dirección y referencias, con los límites de la dirección de un pedido.
  - La misma dirección (código postal y renglón, sin mayúsculas ni espacios de más) es una sola: guardarla otra vez cambia su nombre y sus referencias. Cambiar una a otra que ya está guardada responde «Ya guardaste esa dirección como «Casa».».
  - Se agregan, cambian y borran en Cuenta (`/cuenta`). Borrar una no cambia los pedidos que ya se enviaron ahí.
  - Al pedir a domicilio, la tienda ofrece primero la última usada (no encima de una que el cliente ya empezó a escribir), con «Otra dirección» para escribir una. Una dirección nueva se guarda al enviar si el cliente deja marcado «Guardar esta dirección en mi cuenta» (con nombre opcional) y le queda lugar; con 10, el pedido se envía igual.
  - Mensajes: «Guardas hasta 10 direcciones: borra una para agregar otra.», «Solo una cuenta de cliente guarda direcciones.», «Esa dirección ya no está en tu cuenta.» y «El nombre de la dirección admite hasta 60 caracteres.» (un nombre de hasta 200 se recorta a 60).
- **El mismo pedido, una vez:** la tienda manda una llave por pedido, la misma si se reintenta sin cambiar nada. Con la misma llave el servidor responde el pedido que ya registró, aunque el negocio haya cerrado desde entonces. Con otro teléfono responde «Ese pedido ya se envió.»; si el negocio ya lo canceló, «Ese pedido ya se envió y el negocio lo canceló.»; y una llave vacía, «La clave del pedido no es válida.».
- **Explorar por código postal:** con el código postal del cliente («Entregar en…»), cada negocio que entrega dice «Envío $35.00», «Envío gratis», «… más IVA» si sus precios no lo incluyen, o «No llega a tu código postal». Es la zona activa del negocio que lo incluye, sin su nombre. El mínimo y el envío gratis los aplica el menú al pedir. El código postal se recuerda en el dispositivo y la tienda lo toma la primera vez que el pedido va a domicilio si la cuenta no tiene direcciones.
- **Carrito guardado:** el carrito de cada tienda se guarda en el dispositivo hasta que se envía o se vacía, por 7 días y en 10 tiendas como mucho. Guarda productos, opciones y cantidades, nunca precios: al volver se arma con el menú y los precios de ahora. Lo que no está disponible ahora, o tiene opciones que ya no existen o no forman una elección válida, se queda fuera, y la tienda dice cuántos productos («Agregamos lo que sigue disponible; 1 producto ya no está en el menú de ahora.»). Explorar ofrece «Seguir con el pedido».
- **«Pedir otra vez»:** en Mis pedidos, llena el carrito de esa tienda con el pedido, con la misma regla del carrito guardado. Desde la 24E, cada producto regresa con su nota. Solo regresan cantidades enteras: un producto por kilo o en fracciones no regresa, y la tienda lo dice («1 producto se pide por peso: agrégalo con su cantidad»). Un pedido todo por peso abre el menú sin tocar el carrito que haya.

## Seguimiento del pedido (Fase 24C)

El cliente ve en qué paso va su pedido, con o sin cuenta ([ADR 0023](../adr/0023-order-tracking.md)).

- **Pasos:** los de su tipo de pedido, con los textos que da el servidor:
  - una cocina: Recibido, Confirmado, En preparación, Listo, En camino (solo a domicilio) y Entregado;
  - una tienda: Recibido, Confirmado, En camino (solo a domicilio) y Entregado;
  - un servicio: Recibido, Aceptado, En proceso y Terminado.
- **Horas:** cada paso lleva la primera hora en que el pedido llegó a él. Recibido es la hora del pedido, y En camino, la de su salida con el repartidor. Un paso que el negocio se saltó queda hecho, sin hora. Un pedido entregado o terminado tiene todos sus pasos hechos.
- **En camino:** es el paso actual mientras el pedido salió con el repartidor y no se entregó ni se canceló. No es un estado del pedido (Fase 22D).
- **Cancelado:** el pedido muestra los pasos hasta donde llegó y después «Cancelado» con su hora y el motivo que dio el negocio, el mismo que el cliente recibió por WhatsApp.
- **Detalle en la cuenta** (`/mis-pedidos/:id`): solo un pedido de la cuenta. Muestra los pasos, lo que se pidió con sus opciones y notas, el envío o la visita a domicilio, el total, lo pagado o lo que falta («Por pagar»), la dirección, el enlace de la paquetería si el negocio lo registró, «Pedir otra vez» y «Escribir al negocio». Cualquier otro pedido responde «No encontramos ese pedido en tu cuenta.».
- **Enlace de seguimiento** (`/seguimiento#enlace`):
  - Cada pedido del menú en línea lo recibe al crearse, y el recibo ofrece «Seguir tu pedido» y, a quien compra sin cuenta, «Copiar enlace de seguimiento» («Enlace copiado»; si el navegador no deja copiar, muestra el enlace para copiarlo a mano).
  - Vale 30 días desde el pedido, mientras el negocio está activo.
  - No muestra el nombre, el teléfono, la dirección ni las notas del cliente: solo si va a domicilio.
  - Un enlace desconocido, cambiado o vencido responde «No encontramos ese pedido. El enlace pudo haber vencido.».
  - Los pedidos de antes de la 24C, los del mostrador y los de WhatsApp no tienen enlace.
- **Mis pedidos:**
  - En curso: los pedidos abiertos de los últimos 30 días, cada uno con su paso. Un pedido que el negocio dejó abierto más tiempo pasa a Anteriores.
  - Anteriores: los entregados, terminados y cancelados, por páginas.
- **Se actualiza sola:** el detalle, el seguimiento y En curso se piden otra vez cada 20 segundos mientras el pedido está en curso. Si el enlace vence o el pedido deja de encontrarse con la página abierta, deja de preguntar, dice el mensaje y agrega «Lo de abajo es lo último que vimos.».

## Fotos del servicio (Fase 25)

El negocio guarda fotos del antes y del después de cada servicio ([ADR 0024](../adr/0024-service-photos.md)).

- **Dónde:** en el detalle de una orden de servicio en Pedidos y en el detalle de una pieza en el tablero (Taller). Se agrega una foto con su etapa (antes o después), una nota opcional de hasta 300 caracteres («Mancha en la manga») y la foto: en el teléfono, con la cámara o una ya tomada.
- **Quién y cuándo:** cada foto agregada guarda quién la tomó (el nombre de la sesión y su cuenta) y cuándo (el reloj del servidor). Las que se tomaron al recibir la orden se ven primero, «Al recibir la orden», sin nota ni autor.
- **Cuántas:** hasta 20 fotos agregadas del antes y 20 del después, además de las de la recepción.
- **Cuándo:** cualquier orden de servicio que no esté cancelada recibe fotos. Una entregada o terminada solo recibe fotos del después (la de la entrega): «La orden ya se entregó: solo se le agregan fotos del después.».
- **Quitar:** solo mientras la orden está abierta, y solo quien tomó la foto o un dueño o administrador, después de confirmar. Las fotos de una orden entregada, terminada o cancelada se conservan. Una foto quitada queda en la actividad de la orden.
- **Quién las ve:** quien ve los pedidos (`ManageOrders`) o trabaja el tablero (`ManageServiceBoard`), con el módulo de servicios. El cliente no las recibe.
- **Qué archivo:** solo una foto subida desde la app para el mismo negocio y que esté guardada («Cada foto del servicio debe subirse desde TienditApp para este negocio.»). La misma foto entra una vez («Esa foto ya está en la orden.»).

## Citas en línea (Fase 24D)

El cliente agenda una cita desde el menú en línea del negocio, y el negocio la acepta o la rechaza ([ADR 0025](../adr/0025-online-booking.md)).

- **Cuándo se ofrece:** el dueño prendió «Recibir citas en línea» en Configuración, y el negocio tiene el módulo de agenda, su menú en línea publicado, horario semanal y su sucursal principal con local. Sin uno de ellos, la tarjeta dice cuál falta y el menú no ofrece «Agenda tu cita».
- **Qué se agenda:** los servicios disponibles del catálogo que se venden por hora, por sesión o por servicio, en una categoría activa. Cada uno dura lo que dice su «Duración (min)» (de 5 a 720), o una hora.
- **Horas libres** (`/m/:slug/agendar`):
  - cada media hora desde que abre, y el servicio termina antes de cerrar, en la hora del negocio; los días especiales cuentan;
  - la primera, una hora después de ahora; hasta 30 días adelante, por semanas;
  - una hora después de la medianoche, de una apertura del día anterior, dice su día («12:30 a. m. · Mar 6»);
  - una hora está libre mientras las citas que se cruzan con ella (sin contar canceladas ni las que no llegaron, y con las que esperan confirmación) son menos que las «Citas a la vez» del negocio (de 1 a 20, 1 por omisión).
- **Pedir la cita:** nombre, teléfono (WhatsApp) y notas opcionales; con la cuenta de cliente ya escritos. Un teléfono deja hasta 3 solicitudes esperando en un negocio («Ya tienes citas esperando confirmación. Espera a que el negocio las confirme.»). Si la hora se ocupó mientras, «Esa hora ya no está libre. Elige otra.», y la página vuelve a leer las horas. La misma solicitud enviada dos veces es una cita: si se pierde la respuesta, la página la deja lista para enviarla otra vez («… Si ya se había enviado, no se repite.»).
- **Por confirmar:** la cita ocupa su hora desde que se pide. El cliente recibe por WhatsApp «recibimos tu solicitud de cita… Te avisamos en cuanto la confirmen.».
- **El negocio decide** en la Agenda, donde la solicitud dice «Por confirmar» y «En línea»:
  - Aceptar la programa y avisa «quedó agendada tu cita…»; también se acepta ya empezada, con el cliente ahí, sin ese aviso; una que ya terminó se mueve antes («Esta cita ya terminó: muévela a otra hora antes de aceptarla.»);
  - Rechazar, después de confirmar, la cancela, libera su hora y avisa «no pudimos confirmar tu cita… Elige otro horario en su menú en línea o respóndenos.»;
  - Editar la mueve de hora; el aviso dice que sigue por confirmar;
  - no se cobra hasta aceptarla («Acepta la cita antes de cobrarla.»).
- **Cuántas esperan:** un aviso arriba de la agenda, la campana y «Requiere atención» en Inicio cuentan las solicitudes de ahora en adelante (las de hoy, urgentes) y llevan a la agenda filtrada en «Por confirmar».
- **El cliente sigue su cita:**
  - con el enlace del recibo (`/cita#enlace`), hasta 30 días después de su hora, sin datos del cliente; uno desconocido o vencido responde «No encontramos esa cita. El enlace pudo haber vencido.»;
  - con su cuenta, en Mis citas (`/mis-citas`): Próximas y Anteriores, en todos los negocios;
  - la cancela antes de que empiece, si la agendó en línea, después de confirmar; la agenda dice «La canceló el cliente». Ya empezada: «Tu cita ya empezó o ya pasó: para cambiarla, escríbele al negocio.».
  - Lo que lee: Por confirmar, Confirmada, En curso, Terminada, No asististe, La cancelaste o Cancelada; «No se confirmó» si nadie decidió la solicitud y su hora ya empezó.
- **Sin anticipo:** se cobra en Vender después de aceptarla, como cualquier cita.

## Elegir con quién en las citas en línea (Fase 24J)

[ADR 0032](../adr/0032-online-booking-staff.md):

- **Quién atiende en línea:** el dueño o un administrador elige en Configuración › Quién atiende a las personas del equipo que agendan en línea (activas, con su cuenta activa), hasta 30 encendidas. A cada una le da:
  - el nombre que ven los clientes («Ana L.» por omisión), de 1 a 40 caracteres, sin enlaces, correos ni teléfonos;
  - sus servicios: todos (también los que se agreguen) o los que elija, incluso uno pausado en el catálogo, que vuelve a la página con ella cuando el negocio lo agenda otra vez;
  - su horario: el del negocio o uno propio, con hasta dos horarios por día.
  Apagarla la quita de la página sin perder lo configurado. Cada cambio queda en la actividad del negocio.
- **Con alguien encendido** las horas salen de cada persona y «Citas a la vez» no se usa. Solo se ofrecen los servicios que alguien hace; si nadie hace uno que se agende, el menú, la tienda y Explorar dejan de ofrecer citas y Configuración dice «Nadie de «Quién atiende» hace un servicio que se agenda en línea». Sin nadie encendido, todo sigue como en la Fase 24D.
- **Hora libre de una persona:** dentro del horario del negocio (con sus días especiales) y del suyo; ninguna de sus citas abiertas se cruza (a sus visitas a domicilio se les suma el traslado); y quedan más personas libres que citas sin persona a esa hora, porque una cita sin persona ocupa a alguien. Esa última regla es aproximada: cuenta las citas sin persona que se cruzan con la hora, no a quién le tocaría cada una.
- **El cliente elige** después del servicio «Sin preferencia» (por omisión) o a una persona, y ve sus horas. Ve solo el nombre para clientes.
- **Sin preferencia,** el servidor asigna a la persona libre con menos citas ese día. Con persona, revisa otra vez que siga libre: si no, «Esa hora ya no está libre. Elige otra.»; si dejó de agendar ese servicio (o salió del equipo), «Esa persona ya no agenda ese servicio en línea. Elige otra.». Las solicitudes en línea hacen fila en el candado del negocio, así que dos al mismo tiempo nunca dan a la misma persona; una cita de la agenda guardada en ese mismo instante sí podría cruzarse (la agenda no toma el candado).
- **Dos dueños que cambian a la misma persona al mismo tiempo:** gana el último que guarda, y los dos cambios quedan en la actividad; si los dos la configuran por primera vez, el segundo lee «Alguien más cambió a esta persona al mismo tiempo».
- **La cita** guarda quién la atiende y su nombre completo (hasta 120 caracteres), como la agenda («Atiende: …»), y el recibo, el seguimiento y Mis citas dicen «Te atiende …» con el nombre para clientes de la persona asignada.

## El menú en línea con la forma de las apps de entrega (Fase 24E)

El menú en línea se ve y se usa como una tienda de Rappi o Uber Eats ([ADR 0026](../adr/0026-store-like-delivery-apps.md)). Lo que el pedido cobra, a dónde llega y quién lo recibe no cambia: lo decide el servidor como antes.

- **La tienda:**
  - **Arriba:** la portada que subió el dueño o, sin ella, los colores de su negocio. Encima van el logo, el nombre, la categoría y la línea de Explorar, y si está abierto.
  - **Información:** un botón abre el horario de la semana, los días especiales, la dirección con «Cómo llegar» y el WhatsApp.
  - **Entrega:** con zonas de entrega, la tienda elige «A domicilio» o «Para recoger». Con el código postal de Explorar o de la cuenta dice lo que cuesta el envío o «Por ahora no llegamos al…».
  - **Regresar:** la flecha solo aparece si el cliente llegó desde la app. Un menú abierto desde un QR o un enlace nunca lleva a otros negocios.
- **Encontrar:**
  - Un buscador que compara cada palabra, sin acentos, con el nombre y la descripción.
  - «Destacados», con los productos que el dueño marcó como destacados, hasta 10, si alguno tiene foto.
  - Pestañas de categorías que llevan a su sección.
- **Un producto:**
  - **En la lista:** nombre, precio (con el precio de antes y «−13%» cuando tiene descuento), descripción y foto. El «+» lo agrega de una vez, y si tiene opciones abre su hoja.
  - **En su hoja:** sus opciones con lo que pide cada grupo («Elige 1», «Obligatorio»), sus alérgenos, instrucciones especiales de hasta 300 caracteres y cuántos.
  - **Las instrucciones:** llegan a la cocina con el producto. El mismo producto con otras instrucciones es otra línea del pedido.
- **El pedido:**
  - Un botón flotante «Ver pedido» con cuántos productos lleva y el total.
  - La página del pedido tiene tus productos, la entrega (a domicilio, para recoger con la dirección del negocio, o la mesa), tus datos, el resumen y «Hacer pedido · $X».
- **La portada:** la sube o la quita el dueño o un administrador en Configuración › «Portada del menú». Acepta JPG, PNG, WebP o GIF (solo su primer cuadro) de hasta 5 MB y se ve mejor horizontal, unas tres veces más ancha que alta. Es pública, como el logo.
- **Lo que no muestra:** calificaciones, tiempos de entrega ni costos de servicio, que el negocio no tiene.

## Buscadores y vistas previas (Fase 24D-2)

[ADR 0027](../adr/0027-search-engines-and-link-previews.md):

- **El enlace de una tienda** (`/m/{slug}`), compartido por WhatsApp o en su QR, se ve con su nombre, su línea (o una frase sobre su menú) y su ciudad, y su portada o su logo. Su página de citas se ve con «Agenda tu cita en {Negocio}».
- **En los buscadores** aparecen solo las tiendas que aparecen en Explorar (el dueño las muestra ahí y su menú tiene algo que pedir o agendan en línea), y su página de citas mientras agenda en línea. Una tienda fuera de Explorar conserva la vista previa de su enlace, pero no aparece en los buscadores.
- **Lo que leen los buscadores es lo que el menú ya muestra:**
  - el nombre, la categoría y la línea;
  - la dirección de la sucursal principal (nunca la de una cocina sin local);
  - el teléfono de WhatsApp;
  - el horario de la semana.
- **El resto de la app** (Vender, Caja, Configuración, la cuenta del cliente, el seguimiento) no aparece en los buscadores.

## Productos y favoritos en Explorar (Fase 24F)

[ADR 0028](../adr/0028-product-search-and-favorites.md):

- **Buscar productos:** una búsqueda en Explorar también muestra los productos de todos sus negocios.
  - **Qué compara:** cada palabra, sin acentos, con el nombre y la descripción de lo que cada menú ya muestra (los primeros 500 de cada uno, como el menú). Con categoría, solo los productos de sus negocios.
  - **En qué orden:** primero los que la tienen en el nombre; luego los de los negocios que reciben pedidos ahora.
  - **Cuántos:** 3 de un negocio y 12 en total.
  - **Qué dice cada uno:** su foto (o la inicial en los colores del negocio), su negocio, «Pide en línea» si recibe pedidos ahora, su precio (con el de antes si tiene descuento) y «más IVA» si el pedido le suma IVA (el negocio escribe sus precios sin IVA y el producto no está exento ni al 0 %).
  - **A dónde lleva:** abre la tienda en la hoja del producto, con lo que el menú diga en ese momento. Si el menú ya no lo muestra, abre solo el menú.
- **Favoritos:**
  - **Quién:** solo una cuenta de cliente guarda negocios, con el corazón de su tarjeta en Explorar o el de la portada de su tienda (no en una mesa). Un visitante va a entrar y regresa; el equipo de un negocio no ve el corazón.
  - **Cuántos:** hasta 100. Para agregar otro hay que quitar uno.
  - **Qué se guarda:** solo una tienda con su menú publicado.
  - **Dónde se ven:** «Tus favoritos», arriba de Explorar cuando no hay búsqueda ni categoría, del último guardado al primero.
    - Cada uno lleva su tarjeta mientras aparece en Explorar.
    - Si su dueño lo sacó de Explorar o su menú no tiene qué pedir, queda con su nombre y «No aparece en Explorar ahora».
    - Un negocio suspendido o con el menú apagado no aparece hasta que vuelve.
    - Sus negocios no se repiten en la lista de abajo, ni en su cuenta («N negocios») ni en «Ver más negocios (N)». Uno vuelve a la lista cuando se quita de favoritos. Al buscar o en una categoría no hay «Tus favoritos», así que ahí van en la lista, con su corazón lleno.
  - **Privacidad:** ningún negocio sabe quién lo guardó.
- **La portada en Explorar:** cada tarjeta lleva arriba la portada de su tienda (o sus colores), con el logo encima del borde. La vista previa de Configuración la muestra igual.

## «Cerca de mí» (Fase 24G)

[ADR 0029](../adr/0029-near-me.md):

- **El punto de cada sucursal:**
  - **Quién lo pone:** el dueño o un administrador, en Configuración › Sucursales. Pega las coordenadas que copia Google Maps (o el enlace de un lugar), o toca «Usar mi ubicación» estando en la sucursal. «Revisar en el mapa» abre lo escrito antes de guardar.
  - **Qué se guarda:** latitud y longitud con seis decimales, las dos o ninguna; 0, 0 no se acepta, tampoco un punto que redondeado lo es. Un enlace corto o el de un mapa sin lugar no traen el punto, y el campo lo dice. Vaciar el campo quita la sucursal del mapa.
  - **Qué ve el cliente:** solo el punto de la sucursal principal, y solo si recibe clientes. El de una sucursal solo a domicilio se queda con el equipo, como su dirección.
- **«Cerca de mí» en Explorar:**
  - **Cuándo:** el cliente toca el chip y su navegador le pregunta si comparte su ubicación. Si no la da, el chip dice qué hacer. Un enlace con «Cerca de mí» la pide solo si el navegador ya la permite; si no, espera a que el cliente toque «Usar mi ubicación».
  - **El orden:** los abiertos ahora del más cercano al más lejano, luego los cerrados igual. En cada grupo, los que no están en el mapa van al final.
  - **Qué dice cada tarjeta:** qué tan lejos está en línea recta («a 300 m», «a 1 km», «a 2.5 km», «a 12 km»), a unos 110 m.
  - **Los productos de una búsqueda:** siguen el mismo orden y dicen la distancia de su negocio.
  - **Lo que no cambia:** la categoría, la búsqueda, el código postal y las páginas.
  - **Privacidad:** la ubicación del cliente no se guarda.
- **«Cómo llegar»** abre el punto exacto de la sucursal principal cuando está en el mapa; si no, busca su dirección.

## Reseñas de los negocios (Fase 24H)

[ADR 0030](../adr/0030-business-reviews.md):

- **Quién escribe:** una cuenta de cliente que recibió un pedido del negocio (entregado o completado) o asistió a una cita con él (completada). Una por cuenta y negocio: escribir otra vez la cambia. El equipo de un negocio no escribe reseñas.
- **Qué lleva:** de 1 a 5 estrellas y un comentario opcional de hasta 500 caracteres, sin enlaces, correos ni teléfonos (diez dígitos o más; una fecha o un folio más corto pasan). Se califica desde la tienda, desde un pedido recibido en Mis pedidos o desde una cita atendida en Mis citas.
- **El autor** la cambia o la borra. Una que la plataforma ocultó muestra el motivo, ya no se cambia y se puede borrar. Borrar la cuenta borra sus reseñas.
- **Lo que ve cualquiera:** el nombre y la inicial del apellido del autor («Mariana O.»), sus estrellas, su comentario, cuándo la escribió, si la editó y la respuesta del negocio, de la más nueva a la más antigua y diez a la vez. Nunca su correo, su teléfono ni su cuenta. Un nombre con un enlace, un correo o un teléfono sale como «Cliente».
- **La calificación:** el promedio con un decimal, desde tres reseñas publicadas, en el encabezado de la tienda, en su tarjeta de Explorar y en Clientes › Reseñas. Con menos, la tienda y Clientes › Reseñas dicen cuántas hay y la tarjeta de Explorar no dice nada. Las ocultas no cuentan.
- **El negocio** (dueño o administrador, en Clientes › Reseñas):
  - responde una vez cada reseña publicada; la respuesta se cambia o se quita, con las mismas reglas que el comentario;
  - reporta una reseña con un motivo de la lista (insultos, datos personales, publicidad, no habla de una compra o cita, otro). **Reportar no la oculta:** sigue publicada hasta que la plataforma decide. Una que la plataforma mantuvo se reporta otra vez solo si su autor la cambió después;
  - nunca borra ni oculta una reseña.
- **La plataforma** (Plataforma › Reseñas) revisa las reportadas, la del reporte más antiguo primero: la oculta con un motivo de la lista, o la mantiene (el reporte se cierra). Una oculta se restaura. Cada decisión queda en la actividad del negocio con su motivo.
- **Dos cambios a la vez** sobre una reseña (el negocio la reporta mientras la plataforma la oculta, su autor la cambia mientras el negocio responde) no se pisan: el segundo responde 409 y se vuelve a cargar (`Version`, `rowversion`).
- **Los buscadores** no reciben las reseñas en los datos estructurados de la tienda.

## Pago en línea (Fase 24I)

[ADR 0031](../adr/0031-online-payments.md):

- **La cuenta es del negocio:** el dueño o un administrador conecta la cuenta de Mercado Pago del negocio en Configuración › Pagos en línea y enciende el pago en línea. El dinero llega a esa cuenta; TienditApp no cobra comisión ni ve tarjetas. Sin la configuración de la plataforma no se ofrece.
- **Dónde:** en el menú en línea, para recoger y a domicilio; nunca en una mesa. El cliente elige pagar ahora en línea o al recibir/recoger.
- **Cómo:** tarjeta de crédito, de débito o saldo de Mercado Pago, en una sola exhibición y aprobados al momento. Sin OXXO, transferencias, cajero ni meses.
- **El pedido existe cuando el pago se aprueba:**
  - al pagar, el servidor cotiza el pedido con las reglas del menú y abre un cobro por ese total, que vence en 30 minutos; el carrito se queda en el dispositivo;
  - al aprobarse, el pedido se crea con las reglas del menú de ese momento, con un pago «En línea» por su total y la referencia del pago de Mercado Pago, fechado cuando se registra;
  - lo que el cobro guardaba del cliente se borra al crearse el pedido o al vencer.
- **Se cree solo lo que dice Mercado Pago:** el aviso firmado, el regreso del cliente y un barrido cada dos minutos leen el pago de Mercado Pago con la llave del negocio. Cuenta solo un pago aprobado de ese cobro, cobrado por la cuenta del negocio, en el modo de la plataforma y por el monto exacto en pesos. El mismo pago aplicado dos veces no cambia nada.
- **Si el pedido no se puede crear** (el pago llegó tarde, el negocio dejó de recibir pedidos, un producto ya no está, salió de su horario o se borró, una de sus opciones se agotó, el total cambió o el pago no coincide) el cobro queda «no creado» con el motivo para el cliente y su dinero se devuelve completo. Un segundo pago aprobado del mismo cobro también se devuelve.
- **«En línea» no entra a ninguna caja:** no se registra a mano en una venta ni en la caja, el corte lo muestra aparte, Inicio y ventas por canal dicen cuánto se cobró en línea y el CFDI lleva la forma 31 (intermediario de pagos) con un aviso.
- **Devoluciones:** un reembolso «En línea» (o anular un pedido pagado en línea) pide la devolución a Mercado Pago por la cola, sin pasar dos veces. El pedido dice si Mercado Pago la hizo, la está haciendo o la rechazó; una rechazada (o sin cuenta conectada) la hace el negocio desde su cuenta de Mercado Pago, y desde la 24K TienditApp la ve hecha ahí. Lo pagado en línea se devuelve solo «En línea», como lo cobrado por una plataforma solo como «Plataforma»: así ningún reembolso saca de la caja dinero que nunca entró a ella, ni queda registrado uno que el cliente no recibió.
- **La cuenta no cambia con el pago encendido ni con pagos en curso:** desconectarla o conectar otra pide apagar primero el pago en línea y esperar a que terminen los cobros y las devoluciones en curso. Conectar otra vez la misma cuenta se puede siempre. Desconectar borra sus llaves; lo cobrado sigue en la cuenta de Mercado Pago del negocio.
- **Si Mercado Pago ya no acepta la cuenta** (el dueño quitó el acceso a TienditApp), el menú deja de ofrecer el pago, las devoluciones pendientes las hace el negocio desde su cuenta y Configuración › Pagos en línea pide conectarla otra vez. Los cobros en curso que llegaron a Mercado Pago no le dicen al cliente que no se le cobró: esperan hasta un día a que la cuenta vuelva, y entonces su pago crea el pedido o se devuelve.
- **El enlace de seguimiento** de un pedido pagado en línea solo aparece en el dispositivo que empezó el pago.
- **Pagar otra vez el mismo carrito** desde la tienda vuelve al cobro que ya estaba abierto mientras le queda tiempo; un carrito distinto abre otro cobro.

## Lo que pasa en Mercado Pago (Fase 24K)

[ADR 0044](../adr/0044-online-payment-reconciliation.md):

- **Cómo se entera:** con el aviso de «Pagos» de Mercado Pago y con una revisión cada 15 minutos de los pagos de la cuenta que cambiaron (la primera, desde su cobro pagado más viejo o desde que se conectó, a lo más 180 días), que siempre avanza: lee 200 por pasada y sigue donde se quedó. Todo se lee de Mercado Pago con la llave del negocio, y cuenta solo un pago del cobro, de la cuenta del negocio y del modo de la plataforma.
- **Una devolución aprobada que TienditApp no pidió** (hecha desde la cuenta del negocio, por Mercado Pago o por una reclamación) se vuelve un reembolso «En línea» del pedido que creó ese pago: por su monto, hasta lo que queda pagado en línea, por «Mercado Pago», con el id de la devolución, el motivo de quién la hizo y sin regresar artículos. Se registra cuando TienditApp se entera: no reescribe un día cerrado. Un pedido cancelado no la recibe.
- **Antes, la busca entre las suyas** del mismo pago: la que ya tiene su id; una pedida y en curso del mismo monto (la deja a la cola); una hecha sin id del mismo monto (le anota el id); o una que Mercado Pago rechazó del mismo monto, que el negocio hizo desde su cuenta: queda «hecha por el negocio en Mercado Pago» y deja de contar como rechazada. Si Mercado Pago rechaza una que pidió la cola porque ya estaba devuelta, la cola lo ve enseguida.
- **Una disputa** (contracargo o reclamación) se guarda en el cobro del pedido: abierta mientras Mercado Pago retiene el dinero, perdida o ganada. Una abierta cuyo pago vuelve a aprobarse queda ganada; si se devuelve, perdida. Lo que Mercado Pago dijo antes del último cambio de la disputa no la regresa.
- **Un contracargo perdido** devuelve lo que quedaba pagado en línea, una sola vez por pago, como un reembolso «En línea» por «Mercado Pago». Mientras está abierto no se mueve dinero; uno ganado después de perdido no deshace el reembolso.
- **Dos vías a la vez** (el aviso y la revisión) registran cada devolución y cada contracargo una sola vez: la base rechaza el segundo y la que pierde lee lo que hizo la otra.
- **Se ve:** en el detalle del pedido, de dónde vino cada reembolso «En línea» y un aviso con la disputa; en Configuración › Pagos en línea, los pagos en disputa y hasta cuándo se revisó la cuenta.

## Fiado (Fase 26A)

[ADR 0033](../adr/0033-customer-credit.md):

- **A quién se le fía:** a un cliente del negocio, activo, con teléfono y que no es «Público en general». El dueño o un administrador le pone un límite de $1 a $1,000,000, a centavos, o se lo quita; cada cambio queda en la actividad. Un cliente desactivado conserva su límite, pero no se le fía. Bajar el límite por debajo de lo que debe solo impide fiarle más.
- **Qué se debe** (una sola regla para Por cobrar, el estado de cuenta, el límite y «Debe» en la ficha): el saldo de los pedidos no cancelados, con el teléfono del cliente, que se vendieron fiados o ya se entregaron. No se debe un pedido en curso ni lo que el repartidor salió a cobrar en efectivo (lo debe el repartidor). Los pedidos se juntan por teléfono: +52 y +521 son la misma persona.
- **Fiar en Vender:** solo una venta de mostrador del negocio, con conexión, a un cliente al que se le fía, y la venta tiene que dejar saldo; el cliente puede dejar una parte. Lo que debe más lo que deja la venta no pasa de su límite: si pasa, el mensaje dice cuánto cobrar como mínimo, o que cobre la venta completa cuando ya debe su límite. El servidor lo revisa dentro de un candado por cliente, así que dos cajas que le venden a la vez se cuentan entre sí. La venta se entrega como una pagada (la comida con cocina, cuando está lista; un servicio, cuando sus piezas salen del taller) y queda marcada «Fiado» aunque luego se pague.
- **Abonar a la cuenta:** un monto con un método que entra a caja, repartido del pedido más viejo al más nuevo como pagos de cada pedido en la caja de quien cobra, con la nota «Abono a la cuenta»; el cambio va con la última parte. Nunca más de lo que debe, salvo que se deje el resto a su favor (Fase 26D). Un abono que llega dos veces, o después de otro que la pantalla no vio, encuentra la cuenta cambiada y se rechaza.
- **El estado de cuenta:** el límite, lo que debe y lo que le queda, los pedidos que debe (hasta 100, del más viejo) y sus últimos 10 pagos; «Enviar por WhatsApp» lo manda desde el dispositivo.
- **Clientes › Por cobrar:** una fila por teléfono con lo que debe, cuántos pedidos y desde cuándo, su límite (si hoy se le fía) y si lo rebasa, y el total; busca por nombre o parte del teléfono y ordena por lo que debe o por lo más viejo.
- **Sin** intereses. El plazo y los recordatorios llegaron en la 26B y la 26C, el saldo a favor en la 26D y el aviso antes de vencer en la 26E.

## Condonar y plazo del fiado (Fase 26B)

[ADR 0034](../adr/0034-customer-write-offs-and-credit-term.md):

- **Condonar:** el dueño o un administrador perdona todo o parte de lo que debe un cliente, desde su estado de cuenta: de un pedido o de toda la cuenta (del pedido más viejo al más nuevo), con un motivo (no se va a cobrar, error al registrar, cortesía u otro, que pide una nota). Cada parte queda en su pedido con monto, motivo, nota, fecha y quién lo hizo, y en la actividad del negocio. No mueve la caja ni cambia la venta ni sus pagos; baja lo que se debe.
- **Deshacer lo condonado:** una condonación equivocada la deshace el dueño o un administrador desde el estado de cuenta, una vez. Queda registrada con cuándo y quién la deshizo, y el pedido vuelve a deber ese monto. Un pedido con algo condonado no se cancela hasta deshacerlo, y una condonación con su nota de crédito vigente (Fase 28D) no se deshace hasta cancelar la nota.
- **Qué se condona:** solo lo que el cliente debe (una venta fiada, o un pedido entregado o terminado sin cobrar), nunca lo que cobra un repartidor ni más que el saldo. Si la cuenta cambió desde que se abrió la pantalla (un abono o una venta fiada), se rechaza. Después de condonar, un pago cubre solo lo que queda.
- **Plazo del fiado:** el dueño o un administrador le pone al negocio un plazo de 1 a 120 días, o lo quita. Un pedido vence al día siguiente de su último día para pagar: el día en que se empezó a deber (el de la venta fiada, o el de la entrega de un pedido que se entregó sin cobrar) más el plazo, en la zona horaria del negocio. Solo dice qué está vencido: no cobra intereses ni impide fiar.
- **Lo vencido se ve:** en Por cobrar (por cliente, el total y «Solo vencidos»), en el estado de cuenta (lo vencido y el día límite de cada pedido; el resumen para WhatsApp lo dice) y en Vender, que avisa antes de fiar.
- **Lo que ve el cliente** de su pedido ya no le pide pagar lo condonado.
- **Entregar sin cobrar** sigue igual: pide `ManageOrders` y cuenta como deuda.

## Plazo por cliente y recordatorios del fiado (Fase 26C)

[ADR 0035](../adr/0035-customer-credit-term-and-reminders.md):

- **Plazo por cliente:** el dueño o un administrador le da a un cliente su propio plazo, de 1 a 120 días, o le deja el del negocio, desde «Fiado de…» en Clientes. Cada cambio queda en la actividad. No necesita límite: también cuenta para los pedidos entregados sin cobrar.
- **Qué plazo cuenta:** el del cliente, si no el del negocio. Sale de su ficha por teléfono (una guardada con diez dígitos es la misma persona); si dos fichas comparten teléfono, el más corto. Una cuenta sin ficha usa el del negocio.
- **Lo vencido** se cuenta con el plazo de cada cliente en Por cobrar, el estado de cuenta, Vender y los recordatorios, aunque el negocio no tenga plazo.
- **Recordatorios por WhatsApp:**
  - Los enciende el dueño o un administrador en Por cobrar, con WhatsApp conectado, cada 1 a 30 días (7 por omisión) y con el texto por omisión o una plantilla «Recordatorio del fiado». Apagados en todos al migrar; cada cambio queda en la actividad.
  - A cada cuenta con algo vencido, tenga ficha o no: el primero cuando algo vence, y luego cada N días mientras siga vencido. Uno por cuenta y día.
  - Se encolan de 10:00 a 19:00 del negocio. Al mandarse se omiten si se apagaron, si ya es otro día o pasan de las 20:00, o si ya no debe nada vencido.
  - Nunca a quien escribió BAJA.
  - Sin plantilla aprobada en Meta solo llegan a quien escribió en las últimas 24 horas.
- **Se ve:** «Paga en N días» en Clientes y en Por cobrar; en el estado de cuenta, si el plazo es solo para ese cliente y el último recordatorio.

## El aviso antes de vencer del fiado (Fase 26E)

[ADR 0043](../adr/0043-credit-due-soon-notice.md):

- **Lo enciende** el dueño o un administrador en Por cobrar, junto a los recordatorios, con WhatsApp conectado: de 1 a 7 días antes del día límite (2 por omisión), con el texto por omisión o una plantilla «Aviso antes de vencer». Apagado en todos al migrar y aparte de los recordatorios; cada cambio queda en la actividad.
- **A quién:** a cada cuenta (teléfono) que no debe nada vencido y debe algo cuyo día límite, por su plazo, cae de hoy a dentro de esos días. Solo cuenta lo que debe desde antes de hoy: nunca el mismo día en que se le fió. Quien tiene algo vencido recibe, si está encendido, el recordatorio de lo vencido.
- **Cuántos:** uno por cuenta y por su próximo día límite, a lo más uno al día. Si paga lo de ese día y lo siguiente cae dentro de los días, recibe el aviso de ese nuevo día.
- **Cuándo:** se encola de 10:00 a 19:00 del negocio. Al mandarse se omite si se apagó, si ya es otro día o pasan de las 20:00, si ya tiene algo vencido, o si ya no debe nada que venza ese día (pagó, se le condonó o le cambiaron el plazo).
- **Nunca** a quien escribió BAJA. Sin plantilla aprobada en Meta solo llega a quien escribió en las últimas 24 horas.
- **El mensaje** dice lo que debe en total (`{monto}`), lo que vence (`{porvencer}`) y el día (`{fecha}`).
- **Se ve:** en Por cobrar («Aviso 2 días antes de vencer»), en el estado de cuenta (el último aviso) y en Campañas › Recordatorios del fiado.

## Saldo a favor del cliente (Fase 26D)

[ADR 0036](../adr/0036-customer-balance.md):

- **De quién es:** de la cuenta de un teléfono, como el fiado, tenga ficha o no. Sin teléfono no hay saldo a favor.
- **Dejar a favor** (quien cobra, `ChargeOrders`): un monto de hasta $1,000,000, a centavos, con cómo entra (efectivo, tarjeta, transferencia, billetera u otro), referencia y nota. Entra a la caja de quien cobra, o a la única abierta; sin caja abierta no se puede. Queda en la actividad.
- **El resto de un abono:** «Dejar el cambio a su favor» en Abonar deja a su favor lo que pasa de lo que debe, en vez de dar cambio. Solo con un método que entra a caja.
- **Pagar con él:**
  - en Vender, en una venta de mostrador con el teléfono del cliente, con conexión y no de una plataforma;
  - en el estado de cuenta, con «Usar saldo a favor», del pedido más viejo al más nuevo, hasta lo que debe o lo que tiene, lo que sea menos.

  No en Pedidos ni a mano en la caja. Sin propina. No entra a ninguna caja, porque ya entró cuando lo dejó.
- **Reembolsar a él:** «Saldo a favor» en Reembolsar, si el pedido tiene teléfono. Queda a favor de ese teléfono y no sale de la caja. Anular una venta pagada con saldo lo regresa al saldo.
- **Devolverlo** (quien cobra y reembolsa, `ChargeOrders` y `ProcessRefunds`): hasta lo que tiene, con un método en persona; sale de la caja de quien lo devuelve. Queda en la actividad.
- **Nunca dos veces:** cada pantalla manda el saldo que mostraba (obligatorio para todo lo que lo mueve) y la cuenta lo que debía. Si cambió (otro cajero, un reintento), se rechaza con el saldo nuevo. Todo lo que toma del saldo se revisa en el servidor dentro del candado del cliente.
- **Se ve:**
  - «A favor $X» en el estado de cuenta (con sus últimos 10 movimientos), la ficha, Por cobrar y el aviso de Vender;
  - el total a favor de los clientes en Por cobrar;
  - en el corte del día, sus líneas;
  - «Tienes $X a tu favor» en el estado de cuenta por WhatsApp.
- **Sin** intereses, saldo sin teléfono, anticipos ligados a un pedido o una cita, ni CFDI del anticipo.

## La factura del pedido (Fase 28A)

[ADR 0045](../adr/0045-order-invoicing.md):

- **Qué se factura:** un pedido a un cliente con RFC, con un CFDI 4.0 de ingreso timbrado por el PAC de la plataforma (SW Sapien). Los conceptos, el IVA, la forma de pago y el total son los de la vista previa del CFDI (Fase 3); el backend los decide y el PAC solo los valida.
- **Cuándo se puede:**
  - la vista previa no tiene bloqueos y el receptor no es público en general (esas ventas esperan la factura global);
  - el pedido está pagado por completo (PUE), o el cliente ya lo debe (se vendió fiado, o se entregó o completó) y se factura en parcialidades (PPD, Fase 28C); no está cancelado (sus facturas siguen en «Factura» para descargarlas y cancelar la timbrada con el motivo 03); sin un reembolso en línea en curso. Desde la Fase 28D, una venta con reembolsos o condonación se factura por la venta completa y cada ajuste lleva su nota de crédito;
  - el pago principal no es «Otro», que no es una forma de pago del SAT (un PUE no se declara «por definir», 99);
  - no tiene otra factura viva (timbrando, timbrada o cancelándose; una sustituida no cuenta, Fase 28G) ni está en una factura global viva (Fase 28B);
  - el negocio subió su CSD, vigente y del RFC de Mi negocio, y la plataforma tiene su PAC.
- **El receptor:** RFC, nombre o razón social, código postal, régimen y uso del CFDI como aparecen en su constancia; el uso tiene que corresponder al régimen (G para quien tiene actividad, D para personas físicas, S01 para todos). Pedidos propone los del cliente del teléfono del pedido o, si no tiene, los de la última factura del pedido; «Guardar estos datos en el cliente» los deja en su ficha, y Clientes › Editar › «Cambiar datos fiscales» los cambia; las dos piden `ManageCustomers` y cada cambio queda en la auditoría. Completos o ninguno, y nunca en «Público en general».
- **Folio:** serie del negocio («F» por omisión, de 1 a 10 letras o números) y folio consecutivo por serie, sin huecos. Un rechazo del PAC usa el folio; uno de la base lo regresa.
- **Timbrar:** la factura se guarda antes de llamar al PAC con el CFDI completo y una llave; cada intento manda exactamente ese, así que una respuesta perdida nunca timbra dos veces.
  - timbrada → el XML queda en los archivos privados del negocio con su folio fiscal (UUID), y el PDF si el PAC lo da;
  - rechazada → con el mensaje del PAC; el pedido se puede facturar otra vez con el folio siguiente;
  - sin respuesta → «Timbrando»: «Volver a pedir el timbre» o el worker (cada 2 minutos, hasta 71 horas después de pedirla) la resuelven.
- **Cancelar** (dueños y administradores): con motivo 02 (errores sin relación) o 03 (la venta no se llevó a cabo); se pide primero al PAC y, si se niega, nada cambia. Si el SAT pide la aceptación del receptor, queda «Cancelándose» y el worker pregunta cada 30 minutos: cancelada, o «Timbrada» otra vez si el receptor la rechazó. Una cancelada deja facturar el pedido otra vez con factura y folio nuevos. Nunca se borra una factura.
- **El CSD** (dueños y administradores, Configuración › Facturación): `.cer`, `.key` y la contraseña de la llave. TienditApp revisa que sea de sello, vigente, del RFC de Mi negocio y que la llave corresponda; el PAC lo guarda y TienditApp solo su número, RFC y vigencia. Los archivos y la contraseña nunca se guardan ni se registran. Avisa 30 días antes de vencer; reemplazarlo o quitarlo pide al PAC que olvide el anterior. Un CSD es de un solo negocio de TienditApp: si otro ya lo usa, se rechaza (dos negocios del mismo RFC tramitan uno cada uno).
- **Acceso:** timbrar, volver a pedir, ver y descargar el XML y el PDF, quien gestiona los pedidos (`ManageOrders`); cancelar, el CSD y la serie, dueños y administradores. Otro negocio recibe 404.
- **Sin** envío por WhatsApp, logo en el PDF, otras monedas ni retenciones. La factura en parcialidades y sus complementos de pago llegaron en la Fase 28C, las notas de crédito en la 28D, el envío por correo en la 28E, la autofactura del cliente en la 28F y la sustitución con motivo 01 en la 28G (siguientes secciones de facturas).

## La factura global (Fase 28B)

[ADR 0046](../adr/0046-global-invoice.md):

- **Qué es:** un CFDI 4.0 de ingreso al público en general (XAXX010101000, «PUBLICO EN GENERAL», CP del negocio, régimen 616, uso S01) por las ventas de un periodo que ya terminó y que no tienen factura propia, con InformacionGlobal (periodicidad, mes o bimestre y año). Misma serie, contador de folios, PAC y CSD que las facturas de pedido.
- **El periodo:** diario, semanal (lunes a domingo), quincenal (1 al 15 y 16 al fin de mes), mensual o bimestral (solo con el régimen 621), en días del negocio; las semanas se cortan al terminar el mes. Se emite desde el día siguiente a su último día; pasadas 24 horas del cierre, avisa que el SAT pide emitirla antes. Un año anterior a los 5 que acepta el SAT no se emite.
- **Qué venta entra:** su último cobro cae en el periodo; no está cancelada, su total es mayor a 0, ya no puede cambiar (una cuenta abierta o una venta pendiente esperan a cerrarse), está pagada por completo, no se vendió a crédito, no tiene reembolsos, condonación ni reembolso en línea en curso, no tiene factura propia viva ni está en otra global viva, y sus importes cuadran. Las que tuvieron cobros en el periodo y no entran se listan con su motivo. Entran también, con su aviso, las de un periodo anterior que ya tiene su global viva y que no están en ninguna factura viva (se aceptaron o cerraron, o se resolvió su factura o su reembolso, después de timbrarla); las de un periodo sin global esperan la suya. Una venta que cubría una global timbrada y cancelada (con 02 o con 04) vuelve a entrar por lo que era aunque después tuviera una devolución (avisa: la devolución pide su nota de crédito), salvo que después haya tenido su factura propia.
- **Conceptos e importes:** uno por venta con su folio, 01010101, ACT, «Venta», cantidad 1 y su subtotal, con un traslado por cada tasa que cobró (una tasa con solo partidas gratis no lleva traslado); lo no objeto de IVA, otro concepto con el mismo folio. El total es la suma de las ventas; la forma de pago, la de la venta más grande («Otro» la bloquea); PUE. Hasta 2,000 ventas por global.
- **Lo revisado es lo que se timbra:** si las ventas del periodo cambiaron desde la vista previa (cuáles, cuántas o su total), no se timbra y se pide revisarla otra vez.
- **Una venta, una factura:** una venta en una global viva no se factura sola, y una con factura propia no entra en la global; el candado de facturación del negocio lo asegura aunque se pidan a la vez. Si el cliente pide su factura después: cancelar la global con motivo 04, facturar la venta y emitir otra vez la global del periodo (ya no la incluye).
- **Timbrar y reintentar:** como la factura del pedido (misma llave y mismo CFDI en cada intento; el worker sigue las que quedan «Timbrando»). Un rechazo del PAC libera sus ventas para la siguiente global.
- **Cancelar** (dueños y administradores): motivo 04 (un cliente pidió la factura de una de sus ventas) o 02 (errores); no requiere aceptación. Al cancelarse, sus ventas quedan libres. Nunca se borra.
- **Acceso:** todo, dueños y administradores, en Reportes › Factura global; otro negocio recibe 404.
- **Sin** global de ventas a crédito (parcialidades), sustitución (01), egreso relacionado (notas de crédito), global por sucursal ni más de 2,000 ventas por solicitud.

## La factura en parcialidades y los complementos de pago (Fase 28C)

[ADR 0047](../adr/0047-payment-complements.md):

- **La factura PPD:** una venta que el cliente ya debe (fiada, o entregada o completada) y no está pagada por completo se factura en parcialidades: método PPD y forma 99 «Por definir». El resto de las reglas son las de la factura del pedido (sin un reembolso en línea en curso). Una venta que el cliente todavía no se lleva se cobra antes de facturarla, y una fiada que sigue pendiente o es una cuenta abierta se acepta o se cierra antes (todavía puede cambiar su total).
- **El complemento de pago:** un CFDI tipo P (Pagos 2.0) por cada cobro del pedido, también por los que llegaron antes de la factura. Lleva:
  - el receptor de la factura con uso CP01, en ceros y en moneda XXX;
  - el cobro con su fecha, forma y monto;
  - la factura con su UUID, la parcialidad, el saldo anterior, lo pagado y lo que queda;
  - el IVA prorrateado por tasa (el último pago toma lo que queda, para que los complementos sumen la factura).
- **Quién lo emite:** TienditApp, por su cuenta, en cuanto la factura está timbrada y el cobro registrado (el worker, del cobro más viejo al más nuevo, en los negocios cuyo CSD sella); o quien gestiona los pedidos con «Emitir ahora». El SAT lo pide a más tardar el día 5 del mes siguiente al cobro; Pedidos dice la fecha y avisa cuando ya pasó.
- **Uno por cobro:** un cobro tiene a lo más un complemento vivo (timbrando, timbrado o cancelándose), aunque sea de una factura anterior, y una factura nunca dos con la misma parcialidad. Cada cobro conserva su parcialidad y su monto aunque su complemento se rechace o cancele (el cobro sí se recibió): «Emitir otra vez» lo reemplaza con el siguiente folio y la misma parcialidad, y el saldo anterior de cada uno es el total menos los cobros de las parcialidades anteriores, aunque alguno espere volver a emitirse.
- **Sin complemento:** los cobros «Otro» (no son una forma del SAT) y con saldo a favor (un anticipo aplicado); el cobro dice por qué y la vista previa de la factura lo avisa. Tampoco se emiten mientras falte el CSD vigente del RFC de la factura, los datos de Mi negocio o el PAC: esperan.
- **Serie:** «P» por omisión, con sus propios folios; se cambia en Configuración › Facturación junto a la de las facturas.
- **Timbrar, reintentar y cancelar:** como la factura del pedido (misma llave y mismo CFDI en cada intento; cancelar, dueños y administradores, con motivo 02 o 03). Una factura PPD no se cancela mientras tenga complementos vigentes: primero se cancelan ellos.
- **Acceso:** ver, emitir, volver a pedir y descargar, quien gestiona los pedidos (`ManageOrders`); cancelar, dueños y administradores. Otro negocio recibe 404.
- **Sin** sustitución (01), un complemento que cubra varias facturas, otras monedas, la global de ventas a crédito ni un reporte de complementos.

## Las notas de crédito (Fase 28D)

[ADR 0048](../adr/0048-credit-notes.md):

- **Qué es:** un CFDI de egreso (tipo E) relacionado con la factura timbrada del pedido (TipoRelacion 01, su UUID) por lo que se devolvió en un reembolso o se perdonó en una condonación que sigue en pie. Cada ajuste lleva **una** nota, también los que se hicieron antes de timbrar la factura.
- **El documento:**
  - el receptor de la factura con uso G02 (S01 si su régimen no lo admite), método PUE, MXN;
  - forma: la del reembolso (la misma tabla que los complementos) o 15 «Condonación»;
  - un concepto 84111506, ACT, «Devolución» o «Condonación» por cada tasa de IVA de la factura, y otro sin objeto de impuesto para lo que la factura tiene sin traslados.
- **Importes:** el total es exactamente lo reembolsado o condonado. Se reparte entre las tasas de la factura según su peso en el total (la última toma el resto, y si no le alcanza para devolver un centavo de más, lo devuelven las anteriores); en cada una, la base es la parte sin IVA, redondeada, y el IVA lo que queda de la parte.
- **Cuánto acredita:**
  - la nota de un reembolso, hasta el total de la factura menos sus otras notas;
  - la de una condonación, además, hasta lo que la factura todavía debe (su total menos los cobros con complemento y las otras notas de condonación).

  Cuenta la última nota de cada ajuste aunque esté rechazada o cancelada (se emite otra vez); no cuentan las de una condonación deshecha. Un ajuste mayor que lo que queda dice por qué no se emite.
- **En una factura PPD** solo las notas de condonación bajan el saldo anterior de las parcialidades que vienen después de ellas, y lo que se debe; un reembolso devuelve dinero que ya declaró su complemento. La nota que se emite otra vez conserva el lugar de la anterior en esa cadena.
- **Quién la emite:** TienditApp, por su cuenta (el worker, del ajuste más viejo al más nuevo, en los negocios cuyo CSD sella); o quien gestiona los pedidos con «Emitir ahora». Una rechazada o cancelada se emite otra vez a mano.
- **Sin nota:** los reembolsos «Otro» o como saldo a favor del cliente, los de un pedido cancelado (su factura se cancela con el motivo 03) y las condonaciones deshechas; el ajuste dice por qué. Tampoco se emiten mientras falte el CSD vigente del RFC de la factura, los datos de Mi negocio o el PAC: esperan.
- **Serie:** «NC» por omisión, con sus propios folios; se cambia en Configuración › Facturación junto a las de las facturas y los complementos.
- **Timbrar, reintentar y cancelar:** como la factura del pedido (misma llave y mismo CFDI en cada intento; cancelar, dueños y administradores). La nota de un reembolso se cancela solo con 02 (un reembolso registrado no se deshace); la de una condonación, con 02 o 03.
- **Lo que va primero:** una factura no se cancela mientras tenga notas vivas (Pedidos dice cuáles), ni una condonación se deshace con su nota viva o con complementos vigentes cuyo saldo ya la descuenta: primero se cancelan ellos.
- **La global** sigue sin ventas con reembolsos o condonación: se facturan por su cuenta en Pedidos.
- **Acceso:** ver, emitir, volver a pedir y descargar, quien gestiona los pedidos (`ManageOrders`); cancelar, dueños y administradores. Otro negocio recibe 404.
- **Sin** egreso relacionado con una global, notas por descuento o bonificación sin reembolso, una nota de varias facturas o varios ajustes, otras monedas ni sustitución (01).

## La factura por correo (Fase 28E)

[ADR 0049](../adr/0049-cfdi-email.md):

- **Qué se manda:** el XML y el PDF de cada CFDI timbrado de la factura de un pedido (la factura, sus complementos de pago y sus notas de crédito). La global no; las cancelaciones no se avisan (el SAT las avisa en su buzón).
- **A dónde:** al correo de la factura (`ReceiverEmail`, opcional, una sola dirección en ASCII: el SMTP de la plataforma no manda «peña@…»). Sus complementos y notas van al correo que la factura tenga cuando se timbran.
- **Qué se propone al facturar:** el «Correo para facturas» del cliente (se guarda con sus datos fiscales, `ManageCustomers`, y se audita sin guardar la dirección), el de la última factura del pedido o el correo del cliente. «Guardar estos datos en el cliente» guarda también el correo escrito; sin correo, el cliente conserva el suyo. En Clientes, el correo vacío lo quita. Quitar los datos fiscales quita el correo.
- **Cuándo:** en cuanto el CFDI queda timbrado, una vez por timbre (se pide en el mismo guardado; un intento que perdió el timbre no lo pide). Sale del outbox: sin PDF espera dos intentos (sin contarlos como fallidos) y después manda solo el XML y lo dice; un servidor que no lo toma o no contesta en 60 segundos se reintenta, hasta el último intento del outbox (unas 4.7 horas).
- **Cómo termina:** enviado (quizá sin el PDF), o no se envió porque el servidor rechazó la dirección, porque la plataforma no tiene proveedor, porque el CFDI ya no estaba vigente o porque el servidor no lo tomó en ningún intento. Pedidos sigue leyendo la facturación mientras un correo no termina.
- **Cómo sale:** de la dirección de la plataforma, con el nombre «{Negocio} vía TienditApp» y las respuestas al correo de contacto del negocio; asunto con el tipo, el folio y el negocio; adjuntos `{Serie}-{Folio}_{UUID}`.
- **«Enviar por correo»:** quien gestiona los pedidos (`ManageOrders`) manda otra vez un CFDI timbrado a la dirección que escriba; con «Usar este correo para lo que siga de esta factura», cambia el de la factura. Hasta 5 por CFDI y 300 por negocio en 24 horas, contando los automáticos y bajo el candado de facturación del negocio. Otro negocio recibe 404.
- **El historial** de cada correo (a quién, quién lo pidió, cuándo y cómo terminó) no se borra; Pedidos muestra el último de cada CFDI.
- **Sin** WhatsApp, aviso de cancelación, envío desde el dominio del negocio ni saber si el correo rebotó después de que el servidor lo tomó.

## La autofactura (Fase 28F)

[ADR 0050](../adr/0050-self-invoicing.md):

- **Quién la activa:** dueños y administradores, en Configuración › Facturación, solo mientras el negocio puede facturar. Apagarla siempre se puede; se audita cuando cambia.
- **El ticket:** una venta pagada por completo de un negocio con autofactura activa imprime «Factura en línea» con la dirección `/m/{slug}/facturar`, el folio, un código de 10 caracteres (`ABCDE-12345`) y un QR que trae los dos en el fragmento de la dirección. El código es un HMAC del pedido con la llave del negocio: no se guarda, y la llave se conserva al apagarla (los tickets impresos vuelven a servir). Lo imprime también hasta cuándo se puede (el último día del mes en que se pagó). La API decide si lo lleva: no lo lleva un ticket provisional, con saldo o condonado en parte, cancelado, sin importe, con un reembolso, de una cuenta abierta o un pedido pendiente (su total todavía puede cambiar) o de una app de entrega.
- **La página**, sin cuenta: con el folio y el código el cliente ve el ticket y escribe sus datos fiscales y un correo. Un folio que no existe y un código equivocado responden lo mismo; el folio y el código de un negocio no sirven en la dirección de otro. Buscar, facturar y reenviar comparten un límite de 30 solicitudes por IP cada 10 minutos.
- **Qué se factura en línea:** lo que Pedidos facturaría, pagado en una exhibición (PUE), cerrado (una cuenta abierta o un pedido pendiente todavía puede cambiar su total), sin reembolsos, que no sea de una app de entrega, hasta el último día del mes en que se terminó de pagar (en el calendario del negocio). Fuera de eso, la página dice por qué y que la pida al negocio. Todo se revisa otra vez al facturar, bajo el candado de facturación del negocio; si otra solicitud del mismo ticket llegó primero, se dice lo que hizo (timbrándose, timbrada o cancelándose).
- **La factura** es la de Pedidos, pedida por un valor fijo que ninguna cuenta tiene (`OrderInvoice.SelfInvoiceRequester`) y auditada como «Autofactura». Se timbra en la misma solicitud y el XML y el PDF salen al correo escrito (28E). Los datos fiscales escritos no se guardan en el cliente del negocio. Si el PAC la rechaza, la página dice por qué y el cliente corrige.
- **Ya facturado:** la página lo dice; con el RFC de esa factura y un correo se reenvía (límites de la 28E), también mientras el negocio no pueda timbrar. Sin el RFC correcto no se manda nada.
- **Pedidos** marca la factura y el correo que pidió el cliente en línea.

## Sustituir una factura con errores (Fase 28G)

[ADR 0051](../adr/0051-invoice-substitution.md):

- **Qué es:** una factura nueva del mismo pedido, con los datos corregidos, que lleva la timbrada con errores como «Sustitución de los CFDI previos» (`CfdiRelacionados` 04 con su UUID). Primero se timbra la nueva; después se cancela la anterior con el motivo 01 («con relación») y el UUID de la nueva. Solo la factura de un pedido: la global, los complementos y las notas se cancelan con 02 o 03.
- **Quién:** dueños y administradores, en Pedidos, con «Sustituir factura». Guardar los datos en el cliente pide además `ManageCustomers`.
- **Cuándo se puede:** la factura vigente del pedido, timbrada, sin cancelación pendiente, que ninguna otra sustituye y que no sustituye a otra que siga vigente; sin complementos de pago ni notas de crédito vigentes; con el mismo RFC del emisor con el que timbra hoy el negocio; y el pedido se facturaría hoy en Pedidos (el negocio puede timbrar, el pedido no está cancelado ni en una global vigente, y su CFDI está listo). Si no, Pedidos dice por qué.
- **La nueva** es el CFDI que Pedidos timbraría ahora, con los datos y el correo que se escriben (se proponen los de la anterior), un folio nuevo y el mismo camino de cualquier factura: «Timbrando», el worker, el correo al timbrarse. La anterior deja de ser la factura del pedido en la misma transacción, bajo el candado de facturación; si alguien la cambió mientras tanto, no se guarda nada.
- **La cancelación de la anterior:** en cuanto la nueva queda timbrada: en la misma solicitud, al «Volver a pedir el timbre» de la nueva o en la siguiente pasada del worker si la timbra él. Si el PAC no contesta o el SAT todavía no relaciona las dos (puede tardar hasta dos días en recibir la nueva), la anterior sigue timbrada con el motivo y el worker lo intenta en su pasada de cancelaciones durante 72 horas desde que se timbró la nueva (una cancelación que el receptor rechazó antes de sustituirla no lo detiene); después, «Cancelar con relación» a mano. «Cancelar factura» con 02 o 03 se ofrece en la anterior cuando el 01 no pasó o el receptor lo rechazó. Si el SAT pide la aceptación del receptor, queda «Cancelándose» como cualquier cancelación. Si el SAT no acepta el 01, se cancela con 02 («sin relacionarla con la F-n»), como lo pide el SAT.
- **Si el PAC rechaza la nueva**, la anterior vuelve a ser la factura del pedido (o sigue cancelada, si alguien la canceló con 02 o 03 mientras tanto), en una transacción y en dos pasos por el mismo índice; se corrige y se sustituye otra vez.
- **Mientras la anterior espera su cancelación:** los complementos, las notas de crédito y la autofactura van a la nueva; un reembolso o una condonación esperan a que la nueva quede timbrada. La nueva no se cancela ni se sustituye hasta que se cancele la anterior. La venta sigue fuera de la global.
- **El receptor rechaza la cancelación con 01:** la anterior vuelve a timbrada con el motivo, sigue sustituida y el worker no lo intenta otra vez; el negocio pide otra vez «Cancelar con relación» o la cancela con 02.
- **Se ve en Pedidos:** «Sustituye a la factura F-n», «Sustituida por la factura F-n», que la anterior se cancela al timbrarse la nueva y por qué no pasó. Nunca se borra una factura.
- **Acceso:** sustituir y cancelar con relación, dueños y administradores; otro negocio recibe 404.

## Cancelación y trazabilidad

El agregado conserva `CancellationReason`, agrega historial con estado anterior/nuevo/usuario y emite evento con estado anterior/motivo. Sin embargo, el motivo es opcional y `OrderStatusHistory` no guarda el motivo; es una brecha frente a la regla aprobada de conservar motivo, estado anterior, usuario y trazabilidad completa. Toda cancelación futura debe exigir/normalizar la evidencia necesaria sin reescribir historial.

## Servicio y citas

- Una pieza inicia `Accepted`; pasa a `InProgress`, luego `Completed`, o puede cancelarse desde los dos primeros estados.
- Una pieza completada/cancelada no puede asignarse.
- El caso de uso sincroniza estado de la orden desde las piezas trackeables. Los cambios de pieza pasan por `Order.ChangeServicePieceStatus` para que el rowversion de la orden detecte cambios simultáneos.
- Completar una pieza consume su receta en el mismo guardado; si falta stock y el negocio bloquea (por defecto), la pieza no se completa y el mensaje nombra el insumo; con "vender y avisar" se completa y el insumo queda en negativo.
- Las citas tienen su propio enum y método de cambio de estado; deben mantener reglas en el agregado. «Por confirmar» (una cita agendada en línea, Fase 24D) solo sale aceptada, rechazada o cancelada por su cliente.
- Una cita puede ligarse a un servicio del catálogo y a una persona activa del equipo del mismo negocio (sus nombres se toman de ahí); una misma persona no tiene dos citas que se traslapen. Una cita cerrada (completada, cancelada o no presentada) ya no se edita ni se reprograma; reprogramar avisa al cliente por WhatsApp. La agenda abre con las citas de hoy, la más próxima primero; el historial se lee de la más reciente hacia atrás.

## Inventario, compras y caja

- Existencia disponible es `OnHandQuantity - ReservedQuantity`; una salida normal no permite negativo. El consumo de una venta sigue la política de existencias del negocio (bloquear o vender y avisar, ver [inventario](inventory-and-purchasing.md)).
- Archivar una categoría la saca de Vender, del menú en línea y de los formularios; si tiene productos, primero se pasan a otra categoría activa del mismo negocio. Restaurarla la devuelve tal como estaba.
- Reservado no puede ser negativo ni superar existencia.
- Los movimientos tienen líneas positivas y el tipo define el signo aplicado.
- Conteo físico (Fase 27A, [ADR 0037](../adr/0037-inventory-counts.md)):
  - Un conteo es de una ubicación, y una ubicación tiene un solo conteo abierto. Toma lo que tiene existencia ahí (todo o una categoría) o los artículos elegidos, hasta 2,000, y se le agregan artículos mientras está abierto.
  - Cada cantidad contada (de 0 a 9,999,999, con 3 decimales como máximo y entera en piezas) guarda lo que el sistema tenía del artículo en la ubicación cuando se contó por primera vez. La diferencia es contado menos eso.
  - Corregir una cantidad no cambia lo que tenía el sistema; volver a contar es borrarla y contarla otra vez, contra el sistema de ese momento. Corregir o borrar lo que alguien contó queda en la actividad.
  - Aplicar mueve la existencia de ese momento por cada diferencia, con un ajuste de motivo «Conteo físico» valorado al costo promedio: lo vendido después de contar se conserva. Lo que no se contó se queda como está. Si un artículo quedaría en negativo, no se aplica.
  - Aplicar exige la revisión del conteo que se revisó (409 si cambió). Aplicado o cancelado, el conteo ya no cambia (409), y nunca mueve las existencias dos veces.
- Transferencias entre ubicaciones (Fase 27B, [ADR 0038](../adr/0038-inventory-transfers.md)):
  - Una transferencia lleva artículos de una ubicación activa a otra distinta: salen y entran en la misma transacción, todo o nada. Hecha, no cambia; deshacerla es otra transferencia al revés.
  - Lleva de 1 a 200 artículos activos sin repetir, cada uno con una cantidad de más de 0 a 9,999,999 (3 decimales como máximo, entera en piezas). El origen debe tener cada cantidad; si no, se rechaza nombrando cada artículo con lo que hay.
  - La salida y la entrada valen lo mismo: el costo promedio del artículo, si no el último, si no el de referencia. El costo promedio no cambia.
  - La misma llave de la app contesta la transferencia ya hecha, sin mover nada; con otras ubicaciones o artículos se rechaza (409), porque ya hizo otra.
  - Un movimiento manual no puede ser una pierna de transferencia (tipos de transferencia ni motivo «Transferencia»).
- Revisar que el historial cuadre (Fase 27C, [ADR 0039](../adr/0039-ledger-reconciliation.md)):
  - Cada existencia debe ser lo que suman sus movimientos: lo que entra menos lo que sale. La diferencia es la existencia menos el historial.
  - Cuadrar anota esa diferencia en el historial con un ajuste de motivo «Cuadre del historial», sin costo, que no mueve la existencia ni el costo promedio. Solo ese motivo anota sin mover, y solo como ajuste.
  - Se cuadra lo que se revisó: si la diferencia cambió, no se anota nada (409); si ya es 0, se salta. Un movimiento manual no toma ese motivo.
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
