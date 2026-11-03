# ADR 0063: Explorar encuentra los servicios

- Estado: Aceptada en la Fase 29I (2026-11-03).
- Fecha: 2026-11-03
- Continúa: [ADR 0028](0028-product-search-and-favorites.md) (buscar productos entre negocios) y [ADR 0053](0053-home-visit-quotes.md) (oficios a domicilio), último punto de «Después».

## Contexto

Desde la Fase 24F ([ADR 0028](0028-product-search-and-favorites.md)) una búsqueda de Explorar trae los productos de los menús, pero no sus servicios. Un servicio es un producto que se vende por hora, sesión o servicio, y ningún menú lo muestra. Un cliente que busca «corte de cabello» o «fuga» encuentra a los negocios que lo dicen en su línea, no a los que lo ofrecen en su página.

Hay dos lugares donde un negocio muestra sus servicios:

- **Su página de citas** (`/m/{slug}/agendar`, [ADR 0025](0025-online-booking.md)), si agenda en línea. Muestra los primeros 100 por su orden y, cuando dice quién atiende ([ADR 0032](0032-online-booking-staff.md)), solo los que alguien hace.
- **Su tienda**, si recibe visitas para cotizar ([ADR 0053](0053-home-visit-quotes.md), punto 9). Muestra hasta 50 como precios de referencia («desde $500»), sin la visita misma.

La ADR 0053 lo dejó para después, y la deuda técnica lo anota en los oficios.

Tomé las decisiones de la Fase 29I con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Una búsqueda de Explorar también busca servicios**, en su propia sección «Servicios», después de «Productos».
   - **Qué busca:** solo lo que una página del negocio ya muestra:
     - de uno que agenda en línea, lo que ofrece su página de citas: los primeros 100 por su orden y, si dice quién atiende, los que alguien hace;
     - de uno que recibe visitas, lo que su tienda muestra: los primeros 50 (los destacados primero), nunca el servicio que es la visita.
     - **Sin página que lo muestre, no aparece:** un salón que no agenda en línea, un servicio apagado o de una categoría apagada.
   - **Un servicio de las dos páginas** sale una vez, como de la agenda: se puede agendar directo.
   - **Cómo compara:** igual que los productos: cada palabra, sin acentos ni mayúsculas. El que se agenda, con su nombre y su descripción, como su página de citas; el de una visita, solo con su nombre, porque su tienda no muestra la descripción. Con categoría, solo los de sus negocios.
   - **Cuántos y en qué orden:** igual que los productos. Primero los que la tienen en el nombre, luego por el orden de Explorar (o «Cerca de mí») y por nombre. Hasta 3 de un negocio y 12 en total, solo en la primera página.
   - **Con el código postal del cliente**, los servicios de visita de un oficio que no va ahí quedan al final, como su tarjeta, y dicen «No va a tu código postal». Lo que ese negocio agenda en línea en su local no cambia de lugar.
2. **Cada servicio dice lo que dice su página:**
   - **El que se agenda:** su precio, su duración (una hora si no la tiene, como la página de citas), su foto o la inicial en los colores del negocio, y «Agenda en línea».
   - **El de una visita:** su nombre, «desde» su precio, «más IVA» cuando el negocio escribe sus precios sin IVA, y «Visita a domicilio». Como su tienda, sin duración ni foto: la inicial en los colores del negocio.
   - El negocio y su distancia con «Cerca de mí».
3. **A dónde lleva:**
   - **El que se agenda** abre la página de citas con el servicio elegido (`/m/{slug}/agendar?servicio={id}`). Si en esos 30 segundos dejó de agendarse, la página lo dice y ofrece los demás.
   - **El de una visita** abre la tienda, donde están la visita, su costo y «Solicitar visita». Su precio final se dice después de la visita.
4. **Dónde vive:** en el mismo índice en memoria de los productos (`PublicProductIndex`), que lee los servicios en la misma pasada, desde la foto de Explorar, y se rearma con ella. Sin migración ni tablas nuevas.
   - **Su propio tope:** hasta 10,000 servicios por instancia, de los negocios que Explorar muestra primero; si hay más, lo anota en el log.
   - **Una lectura entre negocios** de solo lo que sus páginas muestran, cada servicio con su negocio, en la lista de lecturas permitidas.
   - **Si el índice no se puede armar,** la búsqueda responde con los negocios, sin productos ni servicios, hasta la siguiente foto. Si solo fallan los servicios, se queda con los productos.
5. **La búsqueda dice** «Busca un negocio, producto o servicio» y su etiqueta, «Buscar negocios, productos y servicios».
6. **Fuera de esta fase:**
   - **Abrir la tienda en el servicio de una visita:** su tienda los lista sin enlace propio.
   - **Encontrar a un oficio «Solo a domicilio» por su ciudad** ([ADR 0053](0053-home-visit-quotes.md)): sigue sin mostrarla.
   - **Un índice de texto en la base** y lo que pide: sinónimos, errores de dedo, plurales.

## Consecuencias

- **Lo que logra:**
  - Quien busca un servicio encuentra a los negocios que lo ofrecen, con su precio y a un toque de agendarlo o de pedir la visita.
  - Explorar no muestra nada que la página del negocio no muestre: lo que se agenda se agenda, y lo que se cotiza dice que es «desde».
- **Lo que limita:**
  - **Un servicio cambiado** tarda hasta 30 segundos en buscarse así, como los productos.
  - **La memoria del índice crece** con los servicios: hasta 10,000 por instancia, además de los 20,000 productos.
  - **Compara texto**, como los productos: sin sinónimos ni errores de dedo. El de una visita, solo con su nombre.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - la sección propia, después de «Productos»;
  - el servicio de las dos páginas, como de la agenda;
  - el de una visita abre la tienda, no la solicitud;
  - el tope de 10,000.
- **Reversibilidad:** no hay migración. Volver a la versión anterior quita la sección «Servicios», y Explorar vuelve a buscar solo negocios y productos.
