# ADR 0071: Los avisos del equipo y la limpieza de Pedidos e Inicio

- Estado: Aceptada en la Fase 29Q (2026-11-06).
- Fecha: 2026-11-06
- Continúa:
  - [ADR 0068](0068-tell-the-business-of-each-request.md) y [ADR 0069](0069-tell-the-business-what-customers-answer.md), «Fuera de esta fase»: que el dueño elija los avisos por su equipo.
  - La deuda técnica de Pedidos e Inicio (Fases 17 y 18): el filtro Canal, `DashboardMetricsDto.OrdersByStatus` y el nombre largo del negocio en 360 px.

## Contexto

Desde las Fases 29N y 29O, cada persona del equipo elige en Mi perfil si le llegan por correo los avisos de la Agenda (solicitudes y cancelaciones) y las respuestas a cotizaciones ([ADR 0068](0068-tell-the-business-of-each-request.md), [ADR 0069](0069-tell-the-business-what-customers-answer.md)). Mientras no elige, solo les llegan a los dueños. El dueño que contrata a una recepcionista tenía que pedirle que entrara a Mi perfil a prenderlos.

Pedidos e Inicio tenían tres pendientes:

- **El filtro Canal** salía aunque el negocio solo vendiera en mostrador. El cliente no sabe si el negocio tiene un número de WhatsApp, y decidirlo por el permiso de WhatsApp se lo ocultaba a los cajeros.
- **`DashboardMetricsDto.OrdersByStatus`** ya no lo mostraba ninguna pantalla desde la Fase 17, pero la API lo seguía mandando.
- **En un teléfono de 360 px**, un nombre largo de negocio se recortaba en el selector de la barra superior.

El dueño del producto pidió avanzar estos puntos, entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29Q con esa autorización.

## Decisión

1. **Quien administra el equipo elige los avisos de cada persona.**
   - El dueño y los administradores, los mismos que cambian roles y permisos (`RequiresTenantManager`), eligen en Equipo › editar persona, o al invitarla, si le llegan «Citas en línea» y «Cotizaciones».
   - `UpdateEmployeeCommand` y `CreateEmployeeCommand` reciben `requestEmails` y `quoteReplyEmails`: `null` deja el aviso como está. `EmployeeDto.noticeEmails` dice lo que le llega a cada persona (`requests`, `quoteReplies`) y si su correo está confirmado.
   - Solo se guarda lo que cambia lo que le llega (`EmployeeNoticeEmails`). Pedir lo que ya recibe no guarda nada: quien nunca eligió sigue sin elegir.
   - **Nadie elige los del dueño desde Equipo**, ni él mismo: él los elige en Mi perfil. Un cambio de los del dueño se rechaza con «El dueño elige sus avisos por correo en Mi perfil.», antes de tocar nada más de la persona.
   - La persona sigue eligiendo en Mi perfil, y vale lo último que se eligió. Mi perfil le dice que quien administra el equipo también los puede elegir por ella (al dueño no).
   - El formulario solo muestra los avisos que el negocio tiene: «Citas en línea» con citas, «Cotizaciones» con servicios o citas. Si a la persona le falta el permiso, lo dice: «Sin el permiso «Agenda» no le llegan.».
   - Quién recibe cada correo no cambia: lo que se eligió, la cuenta activa con el correo confirmado y que pueda atenderlo ([ADR 0069](0069-tell-the-business-what-customers-answer.md)).
2. **La API dice dónde recibe pedidos el negocio.**
   - `GET /api/v1/orders/channels` (`ManageOrders`, como la lista de Pedidos) devuelve, en el orden del filtro:
     - el mostrador, siempre;
     - WhatsApp, mientras el negocio tenga un número (prendido o pausado);
     - el menú en línea, mientras tome pedidos;
     - cada plataforma de reparto que configuró, activa o no.
   - Un canal pausado sigue, para que sus pedidos se encuentren.
   - Pedidos muestra el filtro Canal solo con más de un canal, y ofrece los que dice la API.
3. **Inicio deja de recibir `ordersByStatus`.** Sale de `DashboardMetricsDto`. Las tarjetas de Inicio salen del mismo conteo por estado, que se queda.
4. **El nombre largo del negocio usa dos líneas** en teléfonos de hasta 575 px, en vez de recortarse: en 360 px, «Taquería Los Compadres» se lee completo. Uno corto se ve igual que antes.
5. **Fuera de esta fase:**
   - Avisar a la persona cuando alguien más cambia sus avisos, o decir quién lo hizo.
   - Elegir por el equipo otros avisos (WhatsApp o del navegador).
   - Ofrecer en el filtro el canal de un menú que dejó de tomar pedidos o de un número de WhatsApp que se quitó.

## Consecuencias

- **Lo que logra:**
  - Quien administra el equipo deja listo quién recibe cada aviso al invitar o editar a alguien.
  - Un negocio de mostrador ya no ve un filtro que no filtra, y uno con WhatsApp, menú en línea o plataformas filtra por los que usa, también los cajeros.
  - La API deja de mandar lo que nadie usa, y el nombre del negocio se lee completo en un teléfono chico.
- **Lo que limita:**
  - **La persona no se entera** de que alguien cambió sus avisos hasta que ve Mi perfil o le llega un correo; el pie del correo dice dónde dejar de recibirlos.
  - **Un administrador puede apagarle los avisos a otro administrador.** Es el mismo alcance que ya tiene sobre sus permisos.
  - **Un canal que el negocio dejó de usar** (quitó el número de WhatsApp o el menú ya no toma pedidos) sale del filtro. Sus pedidos siguen en la lista, con su etiqueta, y se encuentran por fecha o búsqueda. Saberlo por los pedidos de cada canal pediría un índice nuevo.
  - **Un nombre que no cabe en dos líneas** (en 360 px, unos 25 caracteres) termina en «…»; se lee completo al abrir el selector.
  - **Contrato:** quitar `ordersByStatus` cambia la respuesta de `/dashboard/metrics`. El cliente de la versión anterior no lo leía.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - que también los administradores elijan, y que nadie elija los del dueño desde Equipo;
  - que valga lo último que se eligió;
  - qué cuenta como canal;
  - el texto del formulario, de Mi perfil y del rechazo.
- **Reversibilidad:** sin migración. Volver al código anterior quita la elección desde Equipo (lo que se eligió se queda guardado, como si la persona lo hubiera elegido), el endpoint de canales y las dos líneas del nombre, y la API vuelve a mandar `ordersByStatus`.
