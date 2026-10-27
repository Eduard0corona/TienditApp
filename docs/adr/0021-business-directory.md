# ADR 0021: Explorar, el directorio de negocios para los clientes

- Estado: Aceptada
- Fecha: 2026-10-16

## Contexto

Hasta la Fase 23A, TienditApp era una herramienta para negocios. Un cliente llegaba a un menú en línea solo con su enlace o su QR (`/m/{slug}`, [ADR 0011](0011-opening-hours.md)), y una cuenta de cliente solo veía Mis pedidos. Fuera del enlace no había ninguna forma de descubrir los negocios registrados ni de pasar de uno a otro.

La Fase 24A abre TienditApp a los clientes. Un cliente ve los negocios registrados por categoría y compra en línea en los que tienen el menú digital activo y una caja abierta. Es el mismo criterio que ya decide si el menú recibe pedidos. El dueño del producto aceptó las cuatro decisiones del informe «TienditApp para clientes»: D1 a D4, abajo.

## Decisión

1. **Qué negocios lista (D1).** Explorar lista un negocio cuando se cumple todo esto:
   - está activo;
   - su menú en línea está publicado (`OnlineMenuMode` `MenuOnly` u `Orders`);
   - su dueño no lo ocultó (`TenantSettings.ListedInDirectory`, «Aparecer en Explorar», que empieza encendido);
   - su menú muestra al menos un producto: disponible, que no sea un servicio y en una categoría activa del mismo negocio. Es la regla del menú (`GetPublicMenuQuery`), escrita como consulta en `PublicDirectorySnapshot.MenuProducts`.

   Publicar el menú ya era una decisión pública del dueño, y el enlace sigue abriendo el menú aunque el negocio no aparezca en Explorar.
2. **Si recibe pedidos ahora, lo decide una sola regla** (`OnlineOrderAvailability`), y la usan el menú (`GetPublicMenuQuery`), su pedido (`PlacePublicOrderCommand`) y Explorar. Un negocio recibe pedidos cuando cumple todo esto:
   - su menú tiene pedidos;
   - está abierto por su horario y sus días especiales;
   - tiene una caja abierta en su sucursal principal. Una caja de antes de las sucursales, o cualquier caja si no hay principal, también cuenta, como en `OrderCashService.HasOpenDrawerAtBranchAsync`.

   Desde su mesa, el cliente sigue pidiendo fuera del horario, salvo en un día cerrado ([ADR 0012](0012-special-days.md)). Explorar no tiene mesa, así que muestra cerrado a un negocio cerrado. La tarjeta usa las palabras del menú: «Pide en línea», «Pedidos en línea en pausa», «Cerrado · abre mañana a las 9:00» o «Solo menú».
3. **La tarjeta solo lleva datos públicos del negocio:**
   - nombre, logo, categoría y descripción;
   - tema;
   - ciudad de la sucursal principal (nunca la calle);
   - si entrega a domicilio, si está cerrado y si recibe pedidos ahora.

   Nunca lleva el contacto, los datos fiscales ni la dirección. Una cocina que solo entrega ([ADR 0016](0016-delivery-only-branches.md)) tampoco muestra su ciudad: dice «Solo a domicilio».
4. **Once categorías fijas, que elige el dueño (D4):** Comida, Abarrotes, Farmacia y salud, Ropa y accesorios, Electrónica, Ferretería y hogar, Papelería, Belleza y bienestar, Autos, Servicios y Otros (`BusinessCategory`, de 0 a 10, `CK_Tenants_DirectoryCategory`).
   - La categoría sale del giro de la lista del registro, comparado sin acentos ni mayúsculas, o del tipo si el giro no está en la lista (`BusinessCategoryDefaults.Suggest`). Comida va a Comida, un servicio a Servicios y una tienda a Otros, porque una tienda puede ser de cualquier categoría.
   - Sigue al giro mientras el dueño no la decida: el registro y la guía de inicio escriben el giro después de crear el negocio (`Tenant.SetBusinessInfo`). La decide cuando elige otra o cuando guarda su tarjeta, aunque la deje en la que el giro le dio: un `DirectoryProfileUpdated` en la bitácora, la misma señal que detiene el `rollback.sql`. Desde entonces, un giro nuevo solo la sugiere.
   - La migración da a cada negocio la categoría de su giro con la misma regla, y una prueba compara las dos para cada giro y tipo.
   - El dueño la cambia en Configuración › Negocio, tarjeta «Cómo te ven en TienditApp», junto con una descripción de un renglón (hasta 160 caracteres, sin saltos ni separadores de línea) y «Aparecer en Explorar». La tarjeta dice por qué no aparece y qué hacer: publicar el menú, activar productos o encenderlo. Lo cambian dueños y administradores (`[RequiresTenantManager]`), y cada cambio queda en la bitácora como `DirectoryProfileUpdated`. La bitácora guarda que la descripción cambió, no el texto.
5. **`/` lleva a Explorar a visitantes y clientes (D2).** El equipo de un negocio sigue entrando a su trabajo.
   - Cambio del 27 de octubre de 2026 (decisión del dueño del producto): en `/`, un visitante sin sesión ve la página para negocios, la presentación de TienditApp ([ADR 0027](0027-search-engines-and-link-previews.md)). Los clientes con sesión siguen empezando en Explorar, el equipo en su trabajo, y una dirección que no existe sigue llevando a Explorar. Explorar sigue en `/explorar`.
   - Explorar y Mis pedidos usan un marco propio, con los colores de TienditApp y sin el menú del negocio.
   - Desde ahí, un visitante entra o crea su cuenta de cliente, y el equipo regresa a su negocio.
   - La búsqueda queda en la dirección (`/explorar/comida?q=tacos`), así que se puede compartir.
6. **Se compra como hoy (D3).** La tarjeta abre el menú del negocio, donde el cliente pide con su nombre y su teléfono, con cuenta o sin ella. Explorar no agrega un carrito entre negocios ni un pago en línea.
7. **La lectura entre negocios es pública, explícita y acotada:**
   - `GetPublicDirectoryQuery` es anónima (`[AllowAnonymousRequest]`). La ruta es `GET /api/v1/public/directory` y comparte el límite por IP del menú (`public-menu`).
   - Lee sin negocio en contexto. Usa dos `IgnoreQueryFilters` revisados en `TenantFilterBypassAllowlistTests`: uno para los negocios, con sus productos, sucursal principal, cajas y zonas en una sola consulta, y otro para sus días especiales. Cada uno filtra por su propio negocio y trae solo las columnas de la tarjeta.
   - La suite entre negocios lo cubre (`CrossTenantDirectorySqlServerTests`).
8. **Se calcula al leer, sobre una foto de 30 segundos** (`PublicDirectoryCache`):
   - Cada instancia guarda la lista ordenada de todos los negocios que lista, con sus conteos por categoría. La rehace una sola petición a la vez, como mucho cada 30 segundos.
   - Los filtros por categoría y búsqueda (nombre, descripción y ciudad, sin acentos ni mayúsculas) y las páginas de 24 (hasta 48) se aplican sobre esa foto.
   - La foto se rehace con su propio límite de 30 segundos, no con el de la petición: un cliente que se va o cambia la búsqueda no la deja a medias para los demás.
   - Cuando hay más negocios que el tope, el log lo avisa.
   - El orden pone primero a los que reciben pedidos, luego a los abiertos y al final a los cerrados, y dentro de cada grupo los ordena por nombre.
   - Una caja que se abre o un cambio del dueño se ven en Explorar en menos de 30 segundos. El menú siempre responde al momento, y es el que acepta el pedido.

## Consecuencias

- **Lo que logra:**
  - Un cliente descubre negocios por categoría y llega al menú de cada uno sin conocer su enlace.
  - Cada negocio con el menú publicado gana un canal más sin configurar nada. Si no lo quiere, lo apaga con un interruptor.
  - El menú, su pedido y Explorar nunca se contradicen sobre si un negocio recibe pedidos.
- **Lo que limita:**
  - La foto lee como mucho 1,000 negocios (`PublicDirectorySnapshot.MaxBusinesses`), los primeros por nombre. Pasado ese número, o si leer todos cada 30 segundos pesa, lo siguiente es una tabla del directorio que se mantiene con eventos: la apertura y el cierre de caja, el cambio del menú y del horario. Hoy no hay eventos de caja, y no se agregan de paso.
  - Las categorías son fijas, una por negocio. Una categoría nueva necesita migración (la CHECK), enum, cliente y la lista del registro.
  - No hay ubicación ni distancia: la búsqueda encuentra la ciudad de la tarjeta, nunca la de una cocina que solo entrega. Mapa, favoritos, «pedir otra vez» desde Explorar y un carrito entre negocios quedan para después.
  - Con varias instancias, cada una tiene su foto, así que dos peticiones seguidas pueden ver estados distintos hasta por 30 segundos.
- **Reversibilidad:**
  - Volver a la versión anterior de la app no necesita la base. Las columnas tienen DEFAULT, y la versión anterior abre y edita negocios sin conocerlas.
  - `rollback.sql` se detiene con `THROW 50016` en cuanto un dueño decidió cómo se ve su negocio: lo ocultó, escribió su descripción o hay un `DirectoryProfileUpdated` en la bitácora, aunque solo haya cambiado la categoría. Volver a aplicar perdería esa decisión y listaría otra vez al que se ocultó. Si nadie decidió nada, quita las dos CHECK y las tres columnas, y se puede ejecutar otra vez.
