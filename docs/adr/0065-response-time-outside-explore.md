# ADR 0065: Cuánto tarda en responder, fuera de Explorar

- Estado: Aceptada en la Fase 29K (2026-11-04).
- Fecha: 2026-11-04
- Continúa: [ADR 0064](0064-response-time-in-explore.md) (cuánto tarda en responder cada negocio, en Explorar), sus dos primeros puntos de «Fuera de esta fase»: decirlo en la tienda, en la página de citas y en la solicitud de visita, y mostrarle al negocio su propio tiempo.

## Contexto

Desde la Fase 29J la tarjeta de Explorar dice «Suele responder en menos de …» ([ADR 0064](0064-response-time-in-explore.md)). Pero muchos clientes no llegan por Explorar: abren la tienda desde un QR, un enlace de WhatsApp o un buscador, y ahí piden la cita o la visita. En esas páginas no decía nada.

El negocio tampoco sabía qué decía su tarjeta. La vista previa de «Cómo te ven en TienditApp» lo dejaba fuera, y un negocio que tarda un día o más no se enteraba, porque Explorar no lo publica.

En la lista de «Después» de la [ADR 0053](0053-home-visit-quotes.md) solo quedan entradas que necesitan decisiones del dueño del producto: el anticipo por Mercado Pago y el PDF de la cotización. Esta fase toma lo que la ADR 0064 dejó fuera y no necesita decisiones suyas.

Tomé las decisiones de la Fase 29K con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Dónde lo lee el cliente:**
   - **La tienda** (`/m/{slug}`) lo dice entre sus píldoras, junto a la información y la calificación. En una mesa (su QR con `?mesa=`) no lo dice: ahí solo se pide la orden, y se leería como el tiempo de esa orden.
   - **La página de citas** (`/m/{slug}/agendar`) y **la solicitud de visita** (`/m/{slug}/visita`) lo dicen bajo el nombre del negocio.
   - El texto es el de la tarjeta: «Suele responder en menos de una hora», «… de 3 horas», «… de 12 horas» o «… de un día». Lleva un ícono de mensaje respondido, también en la tarjeta, para no confundirse con el reloj del horario que va al lado en la tienda.
2. **De dónde sale:** de la foto de Explorar, con las mismas reglas ([ADR 0064](0064-response-time-in-explore.md)); no hay otra consulta ni migración.
   - Por eso solo lo dice un negocio que Explorar lista, que recibe solicitudes y que responde en menos de un día. La API manda `responseTime` en `PublicMenuDto`, `PublicBookingDto` y `PublicHomeVisitDto`, nulo en los demás casos y en una mesa.
   - **Una foto de hasta 10 minutos.** Explorar la rearma cada 30 segundos; estas páginas leen un solo negocio de ella y aceptan una de hasta 10 minutos (`PublicResponseTime.MaxAge`).
   - **Nunca esperan en fila:** la página que la encuentra más vieja la rearma (hasta 30 segundos, el límite de siempre); las que llegan mientras tanto salen sin el dato, sin esperarla (`PublicDirectoryCache.GetWithoutWaitingAsync`). Si el rearmado falla, ninguna de estas páginas lo vuelve a intentar durante un minuto (`FailureBackoff`); Explorar sí, como siempre.
   - **Lo demás se revisa en el momento:** solo lo dicen mientras el negocio aparece en Explorar y agenda en línea o recibe visitas como está ahora, aunque la foto sea anterior.
   - **Si la foto no se puede leer,** o no pudo leer cuánto tardan los negocios, la página sale igual, sin el dato.
   - La foto se arma igual desde cualquier página: cada lectura entre negocios ignora el filtro de negocio de forma explícita, así que fijar el negocio de la tienda antes no la cambia.
3. **Qué lee el dueño** en «Cómo te ven en TienditApp»:
   - **La vista previa** de su tarjeta dice cuánto tarda, como la ven sus clientes.
   - **Una nota debajo**, mientras Explorar cuenta sus solicitudes: lo lista, y agenda en línea o recibe visitas, como está ahora. Su tramo sale de una foto tan reciente como la de Explorar (30 segundos), y si esa foto no pudo leer cuánto tardan los negocios no dice nada, en lugar de «todavía no contamos»:
     - con un tramo de menos de un día: que se calcula con sus solicitudes en línea de los últimos 90 días y dónde lo leen sus clientes (su tarjeta, su tienda y donde le piden citas o visitas);
     - con un día o más: «Sueles tardar un día o más en responder tus solicitudes en línea, así que tus clientes no lo leen.»;
     - sin suficientes: «Todavía no contamos 3 solicitudes en línea para saber en cuánto sueles responder: una cuenta cuando la respondes, o cuando llega su hora o pasa un día sin respuesta.» No dice cuántas tiene: cuentan desde la primera respuesta guardada ([ADR 0064](0064-response-time-in-explore.md)) y una que espera desde hace menos de un día todavía no.
   - La API manda `countsRequests` y `responseTime` en el perfil del directorio (`DirectoryProfileDto`), con «un día o más» incluido: el dueño lo ve, sus clientes no.
4. **Fuera de esta fase:**
   - **Contar las cotizaciones:** cuánto tarda en mandar la cotización después de la visita. Llegó en la Fase 29S ([ADR 0073](0073-quote-time-and-explore-order.md)).
   - **Los pedidos en línea de los restaurantes.** Llegó en la Fase 29T ([ADR 0074](0074-order-confirm-time.md)): cuánto tarda en confirmarlos.
   - **Defenderse de solicitudes falsas** ([ADR 0064](0064-response-time-in-explore.md), «Lo que limita»). Desde la Fase 29M no cuentan las del propio equipo y cada cliente cuenta una vez ([ADR 0067](0067-requests-that-count.md)).
   - **Ordenar por tiempo de respuesta en otro lugar** que la página de una categoría. Desde la Fase 29S, la lista completa y los productos y servicios de una búsqueda siguen el mismo orden ([ADR 0073](0073-quote-time-and-explore-order.md)).
   - **Avisarle al negocio** cuando su tiempo cambia o cuando tiene solicitudes sin responder. Desde la Fase 29L la Agenda dice para cuándo responder cada una y la campana marca las que vencen hoy o ya vencieron ([ADR 0066](0066-answer-requests-in-time.md)); desde la 29N cada solicitud se avisa en la app al momento y por correo ([ADR 0068](0068-tell-the-business-of-each-request.md)).

## Consecuencias

- **Lo que logra:**
  - El cliente que llega por un enlace sabe, antes de pedir la cita o la visita, si el negocio suele responder pronto.
  - El negocio ve lo que dice su tarjeta y, si tarda un día o más, se entera, aunque sus clientes no lo lean.
- **Lo que limita:**
  - **Un negocio que no aparece en Explorar** no lo dice en ninguna página: la foto solo tiene a los listados.
  - **Un cambio en cuánto tarda** se ve en estas páginas hasta 10 minutos después, y en Explorar y en Configuración hasta 30 segundos después.
  - **La página que encuentra la foto vencida** espera a que se arme, hasta 30 segundos, como Explorar; las que llegan mientras tanto, y durante un minuto después de un fallo, no lo dicen.
  - **Un restaurante que recibe pedidos y agenda en línea** lo dice entre sus píldoras, cerca de «A domicilio» y «Para recoger»: el texto no aclara que habla de las citas, no del pedido.
  - Lo demás es lo de la [ADR 0064](0064-response-time-in-explore.md): horas de reloj, empieza vacío y se puede manipular.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - los 10 minutos de foto para estas páginas;
  - el lugar del texto en cada página;
  - mostrarle al dueño «un día o más».
- **Reversibilidad:** no hay migración. Volver a la versión anterior quita el texto de estas páginas y la nota de Configuración; Explorar sigue igual.
