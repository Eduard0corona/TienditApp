# ADR 0073: Cuánto tarda en mandar la cotización y el orden de Explorar en todas sus listas

- Estado: Aceptada en la Fase 29S (2026-11-07).
- Fecha: 2026-11-07
- Continúa: [ADR 0064](0064-response-time-in-explore.md) y [ADR 0065](0065-response-time-outside-explore.md), «Fuera de esta fase»: contar las cotizaciones (cuánto tarda en mandarlas después de la visita) y ordenar por tiempo de respuesta en otro lugar que la página de una categoría.

## Contexto

Desde la Fase 29J, Explorar dice en cuánto suele responder cada negocio sus solicitudes en línea ([ADR 0064](0064-response-time-in-explore.md)), y desde la 29K lo dicen también su tienda, su página de citas y su solicitud de visita ([ADR 0065](0065-response-time-outside-explore.md)). Pero a un oficio lo que más le importa al cliente es lo que sigue: **cuándo le llega la cotización** después de la visita.

Además, Explorar solo ordenaba por tiempo de respuesta dentro de una categoría. La lista completa y los productos y servicios de una búsqueda seguían por nombre.

El dueño del producto pidió avanzar estos puntos, entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29S con esa autorización.

## Decisión

1. **Cuánto tarda en mandar la cotización.**
   - **Qué cuenta:** las visitas que sus clientes pidieron en línea, que ya pasaron (terminaron y no se cancelaron ni se marcaron como no asistidas), y cuya cotización (su primera versión) se mandó en los últimos 90 días. La espera va desde que terminó la visita hasta que se mandó la cotización; una mandada antes de que terminara la visita no esperó nada, y cuenta cuando la visita pasa.
   - **Como las solicitudes** ([ADR 0067](0067-requests-that-count.md)): no cuentan las del propio equipo, y cada cliente cuenta una vez, por su visita cotizada al último. El teléfono solo se compara en la base, nunca se lee.
   - **Qué dice:** desde 3 visitas, el tramo más corto que reúne al menos la mitad: «Suele mandar la cotización en menos de un día», «… de 3 días» o «… de una semana». Con una semana o más, o con menos de 3 visitas, no dice nada.
   - **Dónde:** en la tarjeta de Explorar de un oficio que recibe visitas, en su página para pedir la visita y, para su dueño, en «Cómo te ven en TienditApp», que también le dice si tarda una semana o más o que todavía no se cuenta.
   - **De dónde:** de la foto de Explorar, como el tiempo de respuesta: una sola lectura entre negocios, filtrada por los negocios listados, que une cada visita con su cotización dentro del mismo negocio. Si no se puede leer, nadie lo dice hasta la siguiente foto, y al dueño no se le dice que todavía no se cuenta.
2. **El orden de Explorar es el mismo en todas sus listas.**
   - La foto queda ordenada por grupo (toma pedidos, agenda en línea o recibe visitas; abierto; cerrado), luego por tiempo de respuesta (el que responde antes primero; el que no tiene suficientes solicitudes, o no toma ninguna, en medio) y luego por nombre.
   - La lista completa, cada categoría y los productos y servicios de una búsqueda siguen ese orden. «Cerca de mí» sigue por distancia.
3. **Fuera de esta fase:**
   - Contar como lenta una visita que nunca se cotizó.
   - Ordenar por el tiempo de cotizar.
   - Los pedidos en línea de los restaurantes (punto 8, Fase 29T).

## Consecuencias

- **Lo que logra:**
  - El cliente sabe, antes de pedir la visita, en cuánto suele llegarle la cotización.
  - El oficio tiene una razón para cotizar pronto.
  - Explorar pone primero a los que responden antes en cualquier lista, no solo en una categoría.
- **Lo que limita:**
  - **Solo cuenta lo que se cotizó:** un oficio que visita y nunca manda la cotización no se ve lento; tampoco lo dice, porque no llega a 3 visitas cotizadas.
  - **Empieza con lo que hay:** cuenta las cotizaciones ya mandadas de los últimos 90 días, así que aparece desde el despliegue en los oficios con 3 o más.
  - **Se puede inflar** con visitas falsas desde varios teléfonos, como el tiempo de respuesta ([ADR 0064](0064-response-time-in-explore.md)), o mandando una primera versión de relleno antes de la visita y el precio real en una corrección: cuenta la primera que se mandó. Es una señal para el cliente, no una garantía.
  - **La espera usa el fin de la visita como está ahora:** si el equipo la movió, se mide desde su nueva hora.
  - **Sin índice por fecha de envío:** cada foto (cada 30 segundos) lee las cotizaciones de primera versión de las visitas en línea de los negocios listados; con el tiempo conviene un índice filtrado por `SentAt`, que pide migración.
  - **En la lista completa,** un restaurante (que no toma solicitudes) queda entre los que responden pronto y los que tardan, dentro de su grupo, igual que en una categoría.
  - **Contrato:** la tarjeta de Explorar, la solicitud de visita y «Cómo te ven» suman `quoteTime`; los clientes anteriores lo ignoran.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - qué cuenta y desde cuándo se mide la espera;
  - los tramos (un día, 3 días, una semana);
  - el orden de todas las listas de Explorar;
  - los textos.
- **Reversibilidad:** sin migración. Volver al código anterior quita el tiempo de cotizar y regresa la lista completa y las búsquedas al orden por nombre.
