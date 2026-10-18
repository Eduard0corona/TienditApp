# ADR 0028: Buscar productos entre negocios, favoritos y la portada en Explorar

- Estado: Aceptada
- Fecha: 2026-10-18

## Contexto

Explorar (ADR 0021) busca negocios por su nombre y su línea. Hoy un cliente que quiere «chilaquiles» solo encuentra a los negocios que lo dicen en su línea, no a los que los tienen en su menú. Las apps de entrega buscan platillos entre todos los negocios y llevan directo al platillo.

Además:

- **Cada visita empieza de cero.** El cliente no tiene dónde guardar los negocios a los que vuelve.
- **Las tarjetas no muestran la portada.** Llevan solo el logo, aunque desde la 24E cada tienda tiene portada.

En la 24F entran las tres cosas. Las reseñas quedan fuera: necesitan moderación y una regla de quién puede escribirlas (solo quien compró).

## Decisión

1. **Una búsqueda de Explorar también busca productos.**
   - **Qué busca:** los productos que el menú de cada negocio de Explorar ya muestra (`PublicDirectorySnapshot.MenuProducts`, la regla del menú público), por su nombre y descripción. Compara sin acentos ni mayúsculas, como la búsqueda de negocios, y exige cada palabra. Con categoría, solo los de sus negocios.
   - **En qué orden:**
     1. primero los productos cuyo nombre tiene las palabras;
     2. luego por el orden de Explorar (los que reciben pedidos ahora primero);
     3. luego por nombre.

     Devuelve como mucho 3 de un negocio y 12 en total, para que una búsqueda muestre varios negocios. Solo los busca la primera página de resultados.
   - **Qué datos lleva:** cada producto lleva lo que su menú ya enseña (nombre, precio, precio antes del descuento, foto de 256 px), el nombre y los colores de su negocio, si recibe pedidos ahora y si sus precios incluyen IVA.
   - **A dónde lleva:** abre la tienda en la hoja del producto: `/m/{slug}?producto={id}`. La tienda la abre una vez, con lo que su menú diga en ese momento (disponible o no, sus opciones). Al cerrarla, la dirección vuelve a la de la tienda (conservando la mesa). Un producto que el menú ya no muestra abre solo el menú.
   - **El índice:** se arma con la foto de Explorar (los mismos negocios, hasta 1,000) la primera vez que una búsqueda lo necesita, y se rearma con la foto siguiente (cada 30 segundos como mucho). Explorar sin búsqueda nunca lo lee. Lee hasta 20,000 productos; pasado eso deja fuera los últimos y lo dice en el log.
   - **Excepción al filtro por negocio:** una petición pública no tiene negocio, así que la lectura cruza negocios (`IgnoreQueryFilters`, en la lista permitida con su razón) y queda acotada a los negocios de la foto.
2. **Negocios favoritos de la cuenta del cliente.**
   - **Una tabla nueva, `FavoriteBusinesses`:**
     - columnas: la cuenta (`UserId`), el negocio (`BusinessId`) y cuándo lo guardó;
     - un índice único por cuenta y negocio;
     - hasta 100 por cuenta.
   - **Pertenece a la cuenta, no al negocio:**
     - no es `ITenantScoped`, y por eso la columna se llama `BusinessId` y no `TenantId`;
     - nadie del negocio la lee: un negocio no sabe quién lo guardó;
     - borrar la cuenta borra sus favoritos (cascada);
     - un negocio con favoritos no se borra (restricción): se suspende.
   - **Solo una cuenta de cliente** (`Customer`) guarda favoritos (`PUT /api/v1/me/favorites/{slug}`), y solo de una tienda con su menú publicado:
     - guardar dos veces no cambia nada;
     - dos pestañas que guardan a la vez dejan uno (el índice único);
     - quitar (`DELETE`) es por el slug, en cualquier estado del negocio;
     - una cuenta del equipo de un negocio recibe 400.
   - **La lista** (`GET /api/v1/me/favorites`):
     - va del último guardado al primero;
     - cada uno lleva su tarjeta de Explorar mientras aparece ahí, con el envío al código postal del cliente; sin tarjeta (su dueño lo sacó de Explorar o su menú no tiene qué pedir) queda con su nombre y su logo;
     - un negocio suspendido o con el menú apagado no aparece hasta que vuelve.
   - **En la app:**
     - un corazón en cada tarjeta de Explorar y sobre la portada de la tienda (no en una mesa);
     - el cliente guarda o quita al momento, y el corazón vuelve si el servidor dice que no;
     - un visitante va a entrar y regresa a donde estaba;
     - el equipo de un negocio no ve corazón;
     - «Tus favoritos» va arriba de Explorar cuando no hay búsqueda ni categoría.
3. **La portada en la tarjeta de Explorar.** Cada tarjeta lleva arriba la portada de su tienda (la variante de 512 px), con el logo sobre el borde, como las apps de entrega. Sin portada lleva los colores del negocio. La vista previa de Configuración («Así se ve tu tarjeta») la muestra igual.

## Consecuencias

- **Lo que logra:**
  - Un cliente encuentra un platillo entre todos los negocios y llega a su hoja en un toque.
  - Guarda los negocios a los que vuelve y los ve primero en Explorar.
  - Las tarjetas se ven como la tienda que abren.
- **Lo que limita:**
  - **La búsqueda de productos:**
    - busca cada palabra dentro del texto (también un pedazo, como «chilaq»), sin sinónimos ni corrección de errores de dedo;
    - no ordena por qué tan cerca está el negocio;
    - un producto cambiado tarda hasta 30 segundos en salir con su nombre nuevo;
    - el índice vive en la memoria de cada instancia: unos pocos MB con menús normales, y unos 70 MB en el peor caso (20,000 productos con descripciones de 1,000 caracteres); más allá hará falta un índice de texto en la base.
  - **Los favoritos:** no avisan nada al cliente (ni promociones ni que el negocio volvió), no se comparten y no se pueden ordenar a mano.
  - **Sin Outbox:** guardar un favorito no publica eventos, así que no hay nada que entregar de forma durable.
- **Reversibilidad:**
  - **Hay una migración** (`AddFavoriteBusinesses`, que solo crea la tabla) con scripts idempotentes en `database/production/`.
  - **Su `Down` se detiene** (error 50021) si alguna cuenta ya guardó favoritos: borrar la tabla los perdería. Se corrige hacia adelante.
  - **La portada y los productos** no cambian la base: volver a la versión anterior los quita.
