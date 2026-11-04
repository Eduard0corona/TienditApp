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
   - Por eso solo lo dice un negocio que Explorar lista, que recibe solicitudes y que responde en menos de un día. La API manda `responseTime` en `PublicMenuDto`, `PublicBookingDto` y `PublicHomeVisitDto`, nulo en los demás casos.
   - **Una foto de hasta 10 minutos.** Explorar la rearma cada 30 segundos; estas páginas leen un solo negocio de ella y aceptan una de hasta 10 minutos (`PublicResponseTime.MaxAge`), y solo la rearman si es más vieja. Una tienda abierta desde un enlace casi nunca espera a que se arme.
   - **La tienda lo revisa en el momento:** solo lo dice mientras el negocio agenda en línea o recibe visitas como está ahora, aunque la foto sea anterior.
   - **Si la foto no se puede leer,** la página sale igual, sin el dato.
   - La foto se arma igual desde cualquier página: cada lectura entre negocios ignora el filtro de negocio de forma explícita, así que fijar el negocio de la tienda antes no la cambia.
3. **Qué lee el dueño** en «Cómo te ven en TienditApp»:
   - **La vista previa** de su tarjeta dice cuánto tarda, como la ven sus clientes.
   - **Una nota debajo**, mientras Explorar cuenta sus solicitudes (lo lista, y agenda en línea o recibe visitas):
     - con un tramo de menos de un día: que se calcula con sus solicitudes en línea de los últimos 90 días y dónde lo leen sus clientes (su tarjeta, su tienda y donde le piden citas o visitas);
     - con un día o más: «Sueles tardar un día o más en responder tus solicitudes en línea, así que tus clientes no lo leen.»;
     - sin suficientes: «Todavía no contamos 3 solicitudes en línea para saber en cuánto sueles responder: una cuenta cuando la respondes, o cuando llega su hora o pasa un día sin respuesta.» No dice cuántas tiene: cuentan desde la primera respuesta guardada ([ADR 0064](0064-response-time-in-explore.md)) y una que espera desde hace menos de un día todavía no.
   - La API manda `countsRequests` y `responseTime` en el perfil del directorio (`DirectoryProfileDto`), con «un día o más» incluido: el dueño lo ve, sus clientes no.
4. **Fuera de esta fase:**
   - **Contar las cotizaciones:** cuánto tarda en mandar la cotización después de la visita.
   - **Los pedidos en línea de los restaurantes.**
   - **Defenderse de solicitudes falsas** ([ADR 0064](0064-response-time-in-explore.md), «Lo que limita»).
   - **Ordenar por tiempo de respuesta en otro lugar** que la página de una categoría.
   - **Avisarle al negocio** cuando su tiempo cambia o cuando tiene solicitudes sin responder.

## Consecuencias

- **Lo que logra:**
  - El cliente que llega por un enlace sabe, antes de pedir la cita o la visita, si el negocio suele responder pronto.
  - El negocio ve lo que dice su tarjeta y, si tarda un día o más, se entera, aunque sus clientes no lo lean.
- **Lo que limita:**
  - **Un negocio que no aparece en Explorar** no lo dice en ninguna página: la foto solo tiene a los listados.
  - **Un cambio tarda hasta 10 minutos** en verse en estas páginas, y hasta 30 segundos en Explorar. Ocultarse de Explorar puede seguir diciéndolo en la tienda hasta 10 minutos.
  - **La primera tienda que se abre** con la foto vencida espera a que se arme, como Explorar.
  - **Un restaurante que recibe pedidos y agenda en línea** lo dice entre sus píldoras, cerca de «A domicilio» y «Para recoger»: el texto no aclara que habla de las citas, no del pedido.
  - Lo demás es lo de la [ADR 0064](0064-response-time-in-explore.md): horas de reloj, empieza vacío y se puede manipular.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - los 10 minutos de foto para estas páginas;
  - el lugar del texto en cada página;
  - mostrarle al dueño «un día o más».
- **Reversibilidad:** no hay migración. Volver a la versión anterior quita el texto de estas páginas y la nota de Configuración; Explorar sigue igual.
