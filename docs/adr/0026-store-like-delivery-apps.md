# ADR 0026: El menú en línea con la forma de las apps de entrega

- Estado: Aceptada
- Fecha: 2026-10-18

## Contexto

El menú en línea (`/m/:slug`, Fase 3) creció fase por fase: horario y dirección (19B), fotos (19C), días especiales (21), pedido a domicilio (22C), cuenta de cliente (24B), seguimiento (24C) y citas (24D). Cada cosa quedó en su lugar, pero la página seguía viéndose como un catálogo:

- un encabezado chico con el logo;
- el horario y la dirección como texto;
- categorías que filtran en lugar de llevarte a su sección;
- tarjetas sin foto grande;
- una ventana de opciones solo para los productos que tienen opciones;
- y el carrito como un formulario largo.

Los clientes de un negocio local ya compran en Rappi, Uber Eats y DiDi Food. El dueño pidió que el menú se parezca lo más posible a esas apps, para que el cliente se sienta en terreno conocido, y dejó las decisiones a la implementación.

El servidor ya mandaba cosas que la página no usaba:

- «Destacado» (`IsFeatured`);
- los alérgenos;
- y aceptaba una nota por producto del pedido (`CreateOrderItemDto.Notes`, la que ya leen la cocina y Pedidos).

## Decisión

1. **La página de la tienda**, como abren una tienda las apps:
   - **Portada:** la foto del negocio a todo lo ancho (nueva, ver 5) o, sin ella, los colores de su tema. El logo va encima.
   - **El negocio:** su nombre, su categoría y su línea de Explorar, y si está abierto. La línea sale aunque el negocio no aparezca en Explorar: es texto público del dueño, y su tarjeta en Configuración lo dice.
   - **Botones bajo el nombre:** uno abre su información (horario de la semana, días especiales, dirección con mapa y WhatsApp) y otro elige «A domicilio | Para recoger». Con un código postal conocido, la tienda dice lo que cuesta el envío o que no llega.
   - Compartir el menú. La flecha de regreso aparece solo cuando el cliente llegó desde la app (Explorar, Mis pedidos): un menú abierto desde un QR o un enlace nunca lleva a otros negocios.
2. **Encontrar el producto:**
   - Un buscador que compara sin acentos cada palabra con el nombre y la descripción.
   - «Destacados»: un carrusel con los productos que el negocio marcó, hasta 10, solo si alguno tiene foto. Sin fotos, mosaicos de iniciales no dicen nada, y el servidor ya los pone primero en su categoría.
   - Pestañas de categorías fijas arriba, que llevan a su sección y se marcan solas al desplazarse. Todas las categorías van en una sola lista. En escritorio, las categorías son una lista a la izquierda y los productos van en dos columnas.
3. **El producto:**
   - **La fila:** nombre, precio (con el precio anterior tachado y el porcentaje de descuento) y descripción a la izquierda; la foto a la derecha con el «+» encima, que dice cuántos lleva el pedido.
   - **El «+»:** agrega el producto de una vez; si el producto tiene opciones, abre su hoja.
   - **La hoja:** se abre al tocar cualquier producto. Tiene su foto grande, descripción y alérgenos, y sus opciones con lo que pide cada grupo («Elige 1», «Elige hasta 2», «Obligatorio»). También tiene instrucciones especiales (la nota del producto, hasta 300 caracteres, la misma regla del servidor) y la cantidad, con «Agregar 2 · $134.00».
   - **La nota:** el mismo producto con otra nota es otra línea, porque la cocina lee cada una. El carrito guardado en el dispositivo la conserva, y «Pedir otra vez» trae cada producto con la suya.
4. **El pedido:**
   - Un botón flotante «Ver pedido» con cuántos productos lleva y el total.
   - El pedido se abre como página de pago, de lado en escritorio y a toda la pantalla en un teléfono. Tiene cuatro secciones: tus productos (con su nota), entrega (domicilio, recoger con la dirección, o la mesa), tus datos y el resumen. Abajo va «Hacer pedido · $X».
   - El recibo pone el folio, el envío y el total en una tarjeta.
   - Todo lo que el pedido ya hacía sigue igual: cuenta de cliente, direcciones, llave del pedido, «¿con cuánto pagas?», mínimo y zonas.
5. **La portada del negocio** (`Tenant.CoverImageUrl`):
   - **Cómo se sube:** como el logo (`POST`/`DELETE /api/v1/auth/my-tenant/cover`, dueño o administrador), en las mismas variantes de ancho y en su carpeta pública `covers/{tenantId}`. Reemplazarla borra la anterior solo después de guardar la nueva.
   - **Qué devuelve el servidor:** el menú público trae la portada en 1024 y 2048 px, la descripción y la categoría del negocio, y la foto grande de cada producto (1024 px) para su hoja.
   - **Dónde se configura:** en Configuración, la tarjeta «Portada del menú», con una vista previa con el logo.
6. **La página de citas** (24D) usa la misma barra con el logo, las mismas filas con foto y el mismo botón flotante.
7. **Lo que no se inventa:** las apps muestran calificaciones, tiempos de entrega y costos de servicio. TienditApp no tiene esos datos, así que la tienda no los muestra.

## Consecuencias

- **Lo que logra:**
  - El cliente reconoce la tienda: portada, pestañas, filas con foto, el «+», la hoja del producto, el botón flotante y la página de pago.
  - La nota por producto llega a la cocina sin escribirla en las notas generales del pedido.
  - Los destacados y los alérgenos que el dueño ya capturaba por fin se ven.
- **Lo que limita:**
  - Un negocio sin portada ni fotos se ve con sus colores y las iniciales. Es correcto, pero menos vistoso: la portada y las fotos son del dueño.
  - Explorar sigue con sus tarjetas de la 24A; mostrar ahí la portada es un siguiente paso.
  - La búsqueda es en el cliente, sobre lo que el menú ya trae (hasta 500 productos).
  - La flecha de regreso depende del historial de la app. Un cliente que abrió el menú en una pestaña nueva desde Explorar no la ve.
  - Una línea del pedido no se edita: se cambia su cantidad o se agrega otra con otras opciones o nota.
- **Reversibilidad:**
  - Volver a la versión anterior de la app funciona sin tocar la base: no conoce la portada y sigue guardando el negocio como siempre.
  - `rollback.sql` de `20261018032200_AddBusinessCover` quita la columna solo si ningún dueño subió una portada. En cuanto hay una, se detiene con el error 50020 sin cambiar nada: se perdería la portada y sus archivos quedarían sin nada que los use. Entonces solo queda avanzar.
