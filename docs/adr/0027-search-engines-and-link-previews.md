# ADR 0027: Que los buscadores y las vistas previas lean las tiendas

- Estado: Aceptada
- Fecha: 2026-10-18

## Contexto

La app arma cada pantalla en el navegador: el servidor manda el mismo `index.html` para todas, con el título «TienditApp» y nada más. Eso tiene dos efectos:

- **Las vistas previas de un enlace salen vacías.** WhatsApp, Facebook y los mensajes leen el HTML sin ejecutar la app. El enlace del menú que el dueño comparte por WhatsApp (o que va en su QR) se ve como «TienditApp», sin nombre, foto ni descripción.
- **Los buscadores ven tiendas casi vacías.** Google ejecuta la app, pero tarde y no siempre; los demás no la ejecutan. No hay `sitemap.xml` ni `robots.txt`, y las pantallas privadas (Vender, Caja, Configuración) podían aparecer en un buscador.

En la 24D quedó como la segunda parte, «buscadores», sin decisiones del dueño.

## Decisión

1. **El servidor escribe el encabezado de las páginas públicas** dentro de `index.html`: el título, la descripción, la dirección canónica, la vista previa (Open Graph y la tarjeta de X) y, en una tienda, sus datos estructurados (schema.org). La app sigue dibujando la página igual.
   - **Una tienda** (`/m/{slug}`): «{Negocio}: pide en línea», «agenda tu cita» o «menú en línea», según lo que hace su menú. La descripción es su línea de Explorar o, sin ella, una frase sobre su menú, más su ciudad. La imagen es su portada (24E) o su logo. Los datos estructurados dicen su tipo (restaurante, abarrotes, tienda…), su dirección (nunca la de una cocina sin local), su teléfono de WhatsApp y su horario de la semana.
   - **Su página de citas** (`/m/{slug}/agendar`): «Agenda tu cita en {Negocio}».
   - **Explorar y sus categorías** (`/explorar`, `/explorar/{categoría}`, y `/`, que lleva a Explorar): el título que la app ya ponía. No leen la base.
   - Todo lo que escribe el negocio se escapa (HTML y JSON): un nombre con `</script>` o comillas no sale de su etiqueta.
2. **Solo lo que el menú ya muestra.** La vista previa sale de una consulta anónima nueva (`GetStorePreviewQuery`) con lo que el menú público enseña a cualquiera con el enlace. Nada privado: ni la dirección de una cocina sin local, ni los datos fiscales, ni el teléfono de la sucursal.
3. **Qué pueden listar los buscadores:**
   - **Las tiendas que aparecen en Explorar.** El dueño ya decide eso en Configuración («Aparecer en Explorar»). Una tienda fuera de Explorar conserva su vista previa (su enlace y su QR se ven bien), pero lleva `noindex`: quien la comparte solo por WhatsApp no aparece en Google.
   - La página de citas solo mientras la tienda agenda en línea.
   - **El resto de la app queda fuera:** cualquier otra pantalla responde con `X-Robots-Tag: noindex`, también `/index.html` por su nombre.
   - Una tienda que no existe o no publicó su menú responde 404 (con la página que lo dice). Si la base no responde, la página abre igual con 503 y `Retry-After`, para que el buscador vuelva en lugar de olvidarla.
4. **`robots.txt` y `sitemap.xml`:**
   - `robots.txt` deja leer la API pública (`/api/v1/public/`), que la app necesita para dibujar las páginas públicas cuando un buscador la ejecuta, cierra el resto de la API y los hubs, y apunta al sitemap.
   - `sitemap.xml` lista Explorar, sus categorías con negocios, cada tienda de Explorar y su página de citas cuando agenda. Sale de la foto de Explorar (24A) que ya se reconstruye cada 30 segundos: no lee la base en cada visita.
5. **La dirección pública** de las ligas que salen de la app (canónica, vista previa, sitemap) es `AppUrl`, la misma de los correos. Sin ella, la de la petición, siempre con https fuera de Development.
6. **Los límites:** la tienda, su página de citas y el sitemap comparten el límite por IP del menú público (`public-menu`).
7. **En la app,** el menú y la página de citas ponen en la pestaña el mismo título que escribió el servidor (Explorar ya lo hacía), también al llegar desde Explorar sin recargar.

## Consecuencias

- **Lo que logra:**
  - El enlace del menú compartido por WhatsApp se ve con el nombre, la descripción y la portada del negocio.
  - Google y los demás buscadores leen cada tienda de Explorar, con su dirección y horario en datos estructurados, y encuentran todas en el sitemap.
  - Las pantallas privadas ya no pueden aparecer en un buscador.
- **Lo que limita:**
  - Las imágenes son WebP, como todas las variantes de la app. Algunas apps antiguas no muestran vistas previas en WebP.
  - Los datos estructurados no incluyen los días especiales ni los productos del menú (precios, fotos); el buscador los lee de la página si la ejecuta.
  - El sitemap lista hasta los 1000 negocios de la foto de Explorar (ADR 0021) y no dice cuándo cambió cada tienda.
  - Una tienda recién sacada de Explorar sigue en el índice del buscador hasta que vuelva a leerla y vea el `noindex`.
  - Configurar `AppUrl` en Azure evita que la dirección canónica dependa del nombre con que llegó la petición.
- **Reversibilidad:** no hay migración. Volver a la versión anterior regresa al `index.html` sin encabezado y quita `robots.txt` y `sitemap.xml`.
