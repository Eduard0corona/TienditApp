# ADR 0013: Pedidos de Rappi, Uber Eats y DiDi Food con captura manual

- Estado: Aceptada
- Fecha: 2026-10-12

## Contexto

Un negocio de alimentos sin local recibe sus pedidos en las apps de Rappi, Uber Eats y DiDi Food. Casi siempre la plataforma le cobra al cliente en su app y cada semana deposita lo vendido menos su comisión, el IVA de esa comisión y las retenciones de ISR e IVA. A veces cobra el negocio: el repartidor de DiDi le paga en efectivo al recoger, y la plataforma descuenta la comisión de un depósito.

Hasta la Fase 21, TienditApp solo conocía tres canales (mostrador, WhatsApp y en línea), y todo cobro y todo reembolso, de cualquier método, entraba a una caja abierta (regla de la PR #40). Para registrar un pedido de Rappi había que cobrarlo con «Otro». Eso tenía varios problemas:

- **Caja:** exigía una caja abierta y decía que había en el cajón un dinero que tenía la plataforma.
- **Reportes:** se mezclaba con los cobros «Otro» de verdad y no había comisión ni neto esperado.
- **Duplicados:** nada decía de qué app venía ni con qué folio, así que no había cómo encontrar un pedido capturado dos veces.
- **Clientes:** podía mandar avisos de WhatsApp o campañas a quien era cliente de la plataforma.

Las decisiones fijas de la fase eran:

- Un solo enum de canales.
- Un pago «Plataforma» fuera de la caja.
- Términos por negocio y plataforma, copiados a cada pedido.
- Captura manual desde Vender.
- Disponible en todos los planes.
- Mismas máquinas de estado.
- Un rollback que se detiene si ya hay datos con los valores nuevos.

## Decisión

1. **Un canal por plataforma, en el mismo enum.**
   - `OrderChannel` agrega `Rappi = 3`, `UberEats = 4` y `DidiFood = 5` al final, sin renumerar.
   - `OrderChannelRules` es la única fuente: la lista `DeliveryPlatforms`, `IsDeliveryPlatform` y el nombre que muestran las pantallas (`DisplayName`).
   - `Order.Channel` no cambia después de crear el pedido, y los reportes dependen de eso.
   - Paquetenvia no es un canal: es un repartidor, y entra en la Fase 22C.
2. **Solo el negocio registra un pedido de plataforma, desde Vender y con el folio de la app.**
   - **Campos nuevos:** `CreateOrderCommand` agrega `ExternalOrderNumber`, `CollectedBy` (sin valor es la plataforma) y `ConfirmPossibleDuplicate`. El menú en línea y el chat nunca crean uno: `Online` sigue rechazado en Vender, y un pedido del chat es de WhatsApp.
   - **Folio:** `PlatformOrderNumber` lo normaliza. Quita los espacios de los extremos y un `#` inicial, borra los espacios de en medio y pasa a mayúsculas. Exige de 1 a 30 letras sin acento, números o guiones, y se lee hasta 60 caracteres antes de normalizar. "  #bc 953 " se guarda como `BC953`.
   - **Reglas del agregado:**
     - lo registra el negocio;
     - no se liga a un cliente ni guarda teléfono;
     - no lleva mesa ni se abre como cuenta;
     - no lleva propina, porque la de la app es del repartidor;
     - es de productos (`OrderType.Retail`): el validador también rechaza `ServiceDetail` y el caso de uso, los productos con unidad de servicio;
     - la plataforma debe estar activa en el negocio.

     El teléfono y la mesa en blanco se guardan como nulos. El validador repite con sus mensajes (400) las que no necesitan leer la base. La plataforma activa la revisa el caso de uso antes de armar el pedido (400 `business_rule`).
   - **Existencias y horario:** la app ya aceptó el pedido, así que las existencias van en `Allow` (vende y avisa con `StockWarnings`) y no se exige el horario de cada producto. Un producto que el negocio apagó (`IsAvailable`) sí se rechaza. Vender carga el catálogo sin el filtro del horario mientras hay una plataforma elegida.
   - **Sin conexión:** un pedido de plataforma se registra con conexión (`CapturedOfflineAt` se rechaza), porque el aviso de folio repetido revisa los pedidos de hoy. Vender no lo guarda en la cola del dispositivo.
3. **El pago «Plataforma» no entra a ninguna caja.** Esto cambia la regla de la PR #40: ahora entra a una caja todo cobro *que recibe el negocio*.
   - **Quién lo crea:** solo el servidor, al registrar un pedido que cobró la plataforma (`Order.RegisterPlatformOrder`). Es por el total, con la misma hora que el detalle y el folio como referencia. No exige caja abierta, no crea movimiento de caja y no pide `ChargeOrders`, porque nadie del negocio maneja ese dinero. La cocina, con `ManageOrders`, puede capturar el pedido de la tableta.
   - **Defensas:** `OrderPaymentMethodRules.EntersADrawer` es la única regla de caja. Lo rechazan los validadores de `CreateOrder`, `AddOrderPayment` y `AddCashTransaction`, `OrderPaymentRegistrar` y `CashSession.RegisterTransaction`. `Order.RegisterPayment` también rechaza el método.
   - **Otros pagos:** un pedido que cobró la plataforma no admite otros pagos.
   - **Devoluciones:** lo que cobró la plataforma se devuelve como «Plataforma», y nada más se devuelve así. `OrderRefundProcessor` no busca caja para esa devolución, así que anular un pedido de Rappi sin caja abierta funciona.
   - **Cuando cobra el negocio:** se cobra como siempre, por la caja, al registrarlo o después desde Pedidos, sin propina.
   - **SAT:** la forma de pago del CFDI es 31, Intermediario pagos (`SatCatalog.PaymentFormIntermediary`).
4. **Términos por plataforma, con revisión; cada pedido copia los suyos.**
   - **`DeliveryPlatformSetting`:** es `ITenantScoped`, con una fila por negocio y plataforma (índice único `(TenantId, Channel)`) y `Revision` como token de concurrencia. Guarda:

     | Término | Límite del dominio | Por omisión |
     |---|---|---|
     | Si está activa | — | — |
     | Comisión | 0 a 60 % | Ninguno: se exige al guardar, porque un neto calculado con un contrato inventado sería falso sin que nadie lo note |
     | Base de la comisión | El total con IVA o el subtotal sin IVA | El total con IVA |
     | IVA de la comisión | 0 a 100 % (la API acepta 16, 8 o 0) | 16 % |
     | ISR retenido | 0 a 20 % | 2.5 % |
     | IVA retenido | 0 a 100 % del IVA del pedido | 50 % |

     Todos llevan hasta dos decimales. La base de datos solo exige de 0 a 100: los topes de política viven en `PlatformTerms` y se pueden relajar sin migración.
   - **Quién los cambia:** dueños y administradores. Cada cambio deja `DeliveryPlatformUpdated` en la bitácora, con los términos antes y después, en el mismo guardado. Una petición que no cambia nada no guarda ni registra, y desactivar no borra nada.
   - **`PlatformOrderDetail`:** uno por pedido, inmutable y `ITenantScoped`. Guarda el folio, quién cobró, la revisión de la configuración, los cinco términos, el total del pedido, la comisión, su IVA, las retenciones, el neto esperado y la hora del registro.
   - **La historia no cambia:** cambiar las tasas nunca reescribe un pedido, igual que `Order.PricesIncludeTax`. A un pedido con detalle no se le agregan productos, porque su comisión se calculó sobre su total.
5. **Fórmulas y redondeo (`PlatformOrderCalculator`).** Todo es `decimal`, y cada componente se redondea una vez a centavos con `MidpointRounding.ToEven`, como `OrderTaxCalculator`. R es ese redondeo:
   - comisión = R(base × % de comisión), sobre el total con IVA o el subtotal según la base;
   - IVA de la comisión = R(comisión × % de IVA);
   - si cobró la plataforma:
     - ISR retenido = R(subtotal × % de ISR);
     - IVA retenido = R(Σ base gravada × tasa × % retenido), sobre los grupos de IVA no exentos con tasa mayor que 0, sin redondear antes el IVA de cada grupo;
     - neto esperado = total − comisión − IVA de la comisión − ISR − IVA retenido;
   - si cobró el negocio: la plataforma no retiene lo que no cobró. Las retenciones son 0 y el neto esperado es −(comisión + IVA de la comisión), lo que descontará de un depósito.

   Así se reproduce al centavo el ejemplo publicado de DiDi: un pedido de $1,000 con IVA incluido, comisión de 30 % sobre el total, ISR de 1 % (la tasa anterior a 2026) e IVA retenido de 50 % da un neto de 574.41, el de su ejemplo. Con el ISR de 2.5 % de 2026 da 561.48. El IVA retenido sale 68.97; con el IVA ya redondeado del pedido saldría 68.96.

   Por lo general las retenciones son pagos a cuenta de los impuestos del negocio, no un costo (para quien optó por el pago definitivo, son su impuesto): el costo del canal es la comisión y su IVA. La propina, el envío y las promociones de la plataforma no entran.
6. **Reportes en base caja, con cifras que siguen al dinero.**
   - **Corte del día:**
     - `PlatformTotal` y `PlatformPaymentsCount` son propios, y `PlatformRefundsTotal` va aparte;
     - «Otros» es ahora el resto: todo método sin cubeta propia;
     - la fila por cajero no incluye lo que cobró la plataforma;
     - `NetCashFlow` lo excluye, porque su devolución tampoco crea un movimiento de caja;
     - `NetCashOnlyFlow` no cambia.
   - **Resumen diario:** `DailySalesSnapshot.PlatformAmount` es nuevo, con 0 por omisión. Los días guardados antes no se recalculan, porque el método no existía.
   - **Ventas por canal (`GET /reports/sales-by-channel`):** se calcula en vivo, sin agregar una dimensión al resumen diario. Lo cobrado cuenta en la fecha de cada pago y lo devuelto en la de cada reembolso, como Inicio y las ventas por sucursal. La comisión, su IVA y las retenciones siguen al dinero: cada pago de un pedido con detalle suma su parte de lo que copió el pedido (importe × pago / total del pedido), y cada reembolso resta la suya. Se redondea una vez por fila, y el neto esperado se deriva de lo redondeado.
   - **Anulaciones:** una anulación otro día compensa ese día, y un rango que cubre los dos queda en cero. Ningún día cerrado cambia: la corrección compensa, no edita ([ADR 0007](0007-tax-and-financial-adjustments.md)).
   - **Invariantes:** I1 a I9 fijan el cuadre con las ventas por sucursal, Inicio, el corte, el resumen diario y el CFDI, y cada una es una prueba con nombre ([sucursales y reportes](../domain/branches-and-reporting.md#ventas-por-canal-fase-22f)).
   - **Lista por pedido (`GET /reports/delivery-platform-orders`):** sirve para comparar folio por folio con el estado de cuenta semanal. Busca en el servidor (`search`, por folio de la app escrito como venga o por `ORD`) sobre todas las páginas. Es de solo lectura: no importa ni concilia nada.
   - **Inicio:** «Cobrado» conserva su significado e incluye lo que cobraron las plataformas. `PlatformCollected` dice cuánto es.
7. **Un aviso de folio repetido en lugar de un índice único.**
   - **Por qué no un índice:** los folios cortos se repiten (los 5 caracteres de Uber Eats, el número de recolección de DiDi). Un índice único rechazaría pedidos reales, y su choque caería en el `catch` de la llave de idempotencia de la venta, el 500 que la Fase 4G ya había cerrado.
   - **El aviso:** con el mismo folio de la misma plataforma ya registrado ese día del negocio, en un pedido ni cancelado ni reembolsado por completo, la API responde 409 `possible_duplicate_order` con un mensaje constante. Agregar el código es compatible ([ADR 0008](0008-api-error-contract.md)).
   - **Cuándo se revisa:** después de buscar la llave de idempotencia (un reintento devuelve el pedido ya creado) y antes de tomar el folio interno.
   - **La confirmación:** Vender pregunta y, si es otro pedido, reenvía la misma petición con la misma llave y `confirmPossibleDuplicate`.
8. **Privilegio mínimo.**
   - **Tasas:** solo las leen y las cambian dueños y administradores (`GET` y `PUT /delivery-platforms`).
   - **Qué plataformas están activas:** `GET /delivery-platforms/channels` lo dice sin tasas, a quien tiene `ManageOrders` o `ViewReports` (desde la Fase 22F-2, también `ManageCatalog`, para Precios en apps).
   - **Dinero de la comisión:** `OrderDto`, el ticket, Cocina y el aviso en vivo nunca lo llevan. La comisión de cada pedido solo se ve en Reportes, con `ViewReports`.
9. **Sin capability ni módulo de plan:** está disponible en todos los planes.
10. **Avisos y clientes.**
    - Un pedido de plataforma nunca manda avisos de WhatsApp, con cualquier política del negocio: `OrderNotificationRules` responde que no antes de revisar la política.
    - Queda fuera de las campañas. No guarda teléfono, y `CampaignAudienceResolver` lo filtra por canal como defensa.
    - Queda fuera de la ficha del cliente, que lo busca por teléfono.
11. **Mismo ciclo de vida que una venta de mostrador.**
    - **Estados:** no hay estados nuevos, y el cierre automático es el mismo (`AutoCloseCounterSales`). Sin Cocina, un pedido pagado queda entregado al registrarse; con Cocina, cuando la cocina lo marca listo. Uno que cobra el negocio y todavía no se cobra sigue abierto.
    - **Anular:** usa el flujo de siempre (`VoidOrderCommand`, sin cambios). Devuelve fuera de la caja lo que cobró la plataforma y cancela si el estado lo permite. Un pedido ya entregado queda entregado y reembolsado por completo.
12. **Una sola migración, que solo agrega, con la guarda 50008 y roll-forward.**
    - **La migración:** `AddDeliveryPlatformOrders` crea las dos tablas y la columna, y no toca `Orders`, `OrderPayments`, `OrderRefunds` ni `CashTransactions`, porque los enums se guardan como `int`, sin CHECK. Su `apply.sql` corre en producción antes del merge.
    - **La guarda:** su `Down` y su `rollback.sql` se detienen con el error 50008 si hay pedidos con canal 3 a 5, o pagos, reembolsos o movimientos de caja con el método 5. Desde el primer pedido de plataforma solo se corrige hacia adelante ([migraciones](../development/database-migrations.md)).

## Alternativas descartadas

- **Usar `Other` como parche:** exige caja abierta, mezcla el dinero de la plataforma con los cobros «Otro» y no deja calcular la comisión ni el neto.
- **Dejar el saldo pendiente hasta la liquidación:** inflaría «Por cobrar» con dinero que la plataforma ya cobró, el pedido no se cerraría solo y no contaría en «Cobrado» hasta el depósito. Sin pago no hay venta.
- **Un índice único del folio:** bloquearía pedidos reales y volvería un 500 (punto 7).
- **Una dimensión de canal en el resumen diario:** obligaría a versionar las cifras, a rehacer el relleno de unas 6.5 horas por negocio y a cambiar su índice único. La decisión de la fase pedía no recalcular días. La consulta en vivo agrega en SQL con los índices que ya existen, en periodos de hasta 366 días.
- **Un perfil fiscal por negocio:** la retención depende de si *cada* plataforma tiene el RFC del negocio (sin él retiene 20 % de ISR y todo el IVA). Un perfil por negocio no puede decir "el RFC solo falta en DiDi".
- **`MidpointRounding.AwayFromZero`:** el repositorio redondea con `ToEven`, y el ejemplo de DiDi se reproduce igual con la base por la tasa.
- **Una capability o un módulo de plan:** la decisión de la fase pide que esté en todos los planes.
- **`AutoInclude` del detalle:** no tiene precedente en el repositorio. Las cinco rutas que devuelven un pedido completo (el reintento con la misma llave, la recarga después de un cobro, la devolución, el detalle y Cocina) hacen `.Include(o => o.PlatformDetail)`, como con `ServiceDetail`, y cada una tiene su prueba.
- **Un CHECK aritmético (neto = total − …):** las pruebas con SQLite comparan `decimal(18,2)` como REAL. Las restricciones solo comparan contra 0, y la identidad la exigen el dominio y las pruebas contra SQL Server.

## Consecuencias

- **Lo que logra:**
  - El negocio registra en segundos el pedido que llegó por la app, con su folio, sin abrir caja si la plataforma cobró.
  - Cocina ve el folio en grande, la bolsa sale con un ticket de empaque y el corte separa lo que está en el cajón de lo que debe depositar cada plataforma.
  - Cada pedido guarda la comisión, las retenciones y el neto que le tocaban, y Reportes los suma por canal y los lista por folio para compararlos con el estado de cuenta.
  - Un folio repetido se pregunta antes de registrarse.
- **Lo que limita:**
  - **Precios:** los pedidos usan los precios del catálogo, y la venta rechaza un total distinto del que calcula el servidor. Si en la app se cobra más, la comisión y el neto calculados salen menores que los reales hasta que haya precio por plataforma. La configuración lo avisaba, y el ticket de empaque va sin precios cuando cobró la plataforma. Resuelto en la Fase 22F-2: cada pedido se cobra con los precios de la app de su plataforma, y el del menú donde no hay uno ([ADR 0014](0014-delivery-platform-prices.md)).
  - **Fuera del neto esperado:** el 35 % que cubre Rappi, los faltantes, el saldo negativo de DiDi por efectivo y las cancelaciones tardías de la plataforma (un pedido ya entregado que la plataforma cancela queda entregado y reembolsado). Mientras no haya liquidación, el neto esperado puede no coincidir con el depósito. Resuelto en la Fase 22G: cada depósito se liquida contra sus pedidos, y lo que no cuadra se explica con ajustes con motivo ([ADR 0015](0015-delivery-platform-settlements.md)).
  - **Estimaciones:** un reembolso parcial compensa la comisión y las retenciones en proporción, y el redondeo por fila puede diferir por centavos del estado de cuenta. En SQL Server, cada parte (importe × pago / total) se calcula con escala 6 antes de sumarse. Con pagos completos el resultado es exacto; con pagos parciales, en un punto medio raro, puede diferir un centavo del cálculo con toda la precisión.
  - **Folio repetido:** el aviso no es una restricción. Dos capturas del mismo folio en el mismo instante pasan las dos; se ven en la búsqueda y en la lista por pedido, y se corrigen anulando una.
  - **Tasas fiscales:** las de 2026 (ISR de 2.5 % e IVA retenido de 50 %; 20 % y 100 % sin RFC) son las de la Ley de Ingresos de la Federación 2026 (publicada en el DOF en noviembre de 2025) y la Ley del IVA, pero dependen de cada caso (persona física o moral, RFC, pago definitivo) y cambian cada año. Son configurables, y la pantalla pide confirmarlas con el contador. La clave 31 y la base de la comisión de Uber Eats (con o sin IVA) también faltan por confirmar.
  - **Folio:** solo se guarda el folio corto. El ID largo de DiDi o de Uber para conciliar no llegó con la liquidación (Fase 22G): sigue pendiente.
  - **Conteos:** `PaidOrders` del reporte por canal cuenta cada pedido una vez por periodo, como Inicio, y las ventas por sucursal lo cuentan por día. Ningún importe depende de esa diferencia.
- **Reversibilidad:**
  - **Volver a la versión anterior** (master con la Fase 21) funciona sin `rollback.sql` mientras no haya pedidos de plataforma: no conoce las tablas ni la columna.
  - **Con pedidos de plataforma,** esa versión sacaría de una caja la devolución de una anulación, y su worker sumaría en `OtherAmount` lo que cobraron las plataformas. Por eso se corrige hacia adelante, y `rollback.sql` se detiene con el error 50008.
  - **Si el swap cruza la medianoche,** la instancia anterior puede guardar el día con lo que cobraron las plataformas en «Otros». La consulta 7 del `verify.sql` lo detecta y `database/production/data-repair/20261012_PlatformAmountSnapshots` lo repara. Por eso se recomienda no publicar entre las 00:00 y las 00:45, hora del negocio.
  - **Lo que borra el rollback:** sin pedidos de plataforma, `rollback.sql` borra la configuración de cada plataforma. La bitácora conserva cada valor.

## Siguientes pasos

- **22F-2:** precio por plataforma para cada producto. Hecho: [ADR 0014](0014-delivery-platform-prices.md).
- **Liquidación semanal y conciliación con el estado de cuenta:** el 35 % que cubre Rappi, los faltantes, el saldo negativo de DiDi por el efectivo que cobra el negocio y las cancelaciones tardías. Hecho: [ADR 0015](0015-delivery-platform-settlements.md).
- **Integraciones** por la API de cada plataforma o por un integrador, en lugar de la captura manual.
- **CFDI de retenciones** que emiten las plataformas.
- **El ID largo del pedido** para conciliar.
