# ADR 0081: Avisar a quien le cambian sus avisos, vigilar las solicitudes falsas y el tope por cuenta

- Estado: Aceptada en la Fase 30A (2026-11-08).
- Fecha: 2026-11-08
- Continúa:
  - [ADR 0071](0071-team-notices-and-orders-cleanup.md), «Fuera de esta fase»: avisar a la persona cuando alguien más cambia sus avisos, o decir quién lo hizo.
  - [ADR 0076](0076-mark-a-request-as-fake.md), «Fuera de esta fase»: que alguien revise las solicitudes que marca un negocio.
  - [ADR 0070](0070-cancellations-cap-agenda-order-and-sound.md), «Fuera de esta fase»: contar en el tope las canceladas de otros teléfonos de la misma persona.

## Contexto

- **Los avisos de otro.** Desde la Fase 29Q, quien administra el equipo elige qué avisos por correo le llegan a cada persona ([ADR 0071](0071-team-notices-and-orders-cleanup.md)). La persona no se enteraba: dejaba de recibir las solicitudes, o empezaba a recibirlas, sin saber por qué.
- **Las solicitudes falsas.** Desde la Fase 29V, un negocio marca como falsa una solicitud ([ADR 0076](0076-mark-a-request-as-fake.md)): no cuenta en su tiempo de respuesta y su teléfono no le pide en 90 días. No tiene tope ni revisión, así que también puede marcar así las reales que dejó pasar. Y un teléfono que molesta a varios negocios solo se frena en cada uno por su lado.
- **El tope por teléfono.** Un teléfono no pide otra cita si tiene 3 esperando, contando las que su cliente canceló en el último día ([ADR 0070](0070-cancellations-cap-agenda-order-and-sound.md)). Quien tiene cuenta y cambia de teléfono empezaba la cuenta de nuevo.

El dueño del producto pidió avanzar estos puntos (el 8, el 11 y el 12), entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 30A con esa autorización. No lleva migración.

## Decisión

1. **Avisar a quien le cambian sus avisos.**
   - Cuando alguien del equipo cambia, en Equipo, los avisos por correo de otra persona, a esa persona le llega un correo: «Mariana Ortiz cambió tus avisos por correo en Estética Luna», con lo que ahora le llega y lo que ya no, y el enlace a Mi perfil.
   - Solo dice lo que cambió. Guardar lo que la persona ya recibe no manda nada, y quien cambia los suyos no se avisa a sí mismo.
   - Solo habla de los avisos que le pueden llegar: los de la Agenda, si puede responder las solicitudes; las respuestas a cotizaciones, si trabaja las cotizaciones. Uno que se prendió sin el permiso que pide no le llegaría ni lo podría cambiar en Mi perfil. Si no queda ninguno, no se manda.
   - Sale por el outbox con el cambio (`TenantUserNoticeEmailsChangedEvent`). Al mandarlo se comprueba, como en los demás avisos, que el negocio no esté suspendido y que la persona siga activa, con su cuenta activa y su correo confirmado. También, que siga recibiendo lo que el cambio dice: si lo cambiaron otra vez, ese cambio manda el suyo.
   - **Quién lo cambió:**
     - alguien del equipo va con el nombre de su cuenta;
     - el administrador de la plataforma que entra al negocio es «El soporte de TienditApp», nunca su nombre;
     - una cuenta que ya no existe es «Alguien de tu equipo».
   - Al invitar a alguien no se manda: la invitación es su primer correo.
2. **La plataforma vigila las solicitudes falsas.**
   - La consola de la plataforma (solo su administrador) muestra «Solicitudes marcadas como falsas», de los últimos 90 días:
     - **Negocios que más marcan:** cuántas siguen marcadas, a cuántas les quitó la marca y cuántas solicitudes en línea de clientes recibió, sin las de su equipo. Las recibidas son las que llegaron en esos días y las marcadas en ellos que llegaron antes, así que nunca son menos que las marcadas. Así se ve quién marca mucho para lo que recibe.
     - **Teléfonos que marcaron varios negocios:** los que siguen marcados por dos negocios o más, con los negocios y la última vez.
   - Cada lista muestra los 20 primeros y lo dice cuando llega a 20.
   - **El teléfono va con sus últimos cuatro dígitos.** La plataforma compara teléfonos, no los lee. Ni la API ni la pantalla mandan el número completo.
   - Es una lectura entre negocios, explícita y solo para el administrador de la plataforma (`RequiresPlatformAdmin`).
   - **No deja registro de auditoría:** las guías piden auditar el acceso entre negocios, pero esta lectura no cambia nada ni entra a un negocio, y solo devuelve conteos, nombres de negocios y cuatro dígitos por teléfono. Es la misma excepción que ya tiene la lista de reseñas de la plataforma ([ADR 0030](0030-business-reviews.md)). Una acción sobre un negocio sí se auditaría.
   - No hace nada por sí sola: es para que alguien revise.
3. **El tope cuenta todos los teléfonos de la cuenta.**
   - Con su sesión de cliente, las solicitudes que esperan y las canceladas del último día cuentan por su teléfono y también por su cuenta, con cualquier otro teléfono. El mensaje es el mismo de siempre.
   - Sin sesión, sigue contando por teléfono, como antes.
   - Vale en la página de citas y en la solicitud de visita. La búsqueda por cuenta usa el índice de las citas de cada cuenta, que ya existe.
4. **Fuera de esta fase:**
   - guardar quién cambió los avisos para mostrarlo en Mi perfil (pide migración);
   - una acción de la plataforma sobre un negocio o un teléfono (quitar la marca, bloquear);
   - un tope de cuántas puede marcar un negocio;
   - que la marca de falsa frene también por cuenta, no solo por teléfono;
   - avisar al negocio cuando la plataforma revisa sus marcas.

## Consecuencias

- **Lo que logra:**
  - Nadie deja de recibir las solicitudes sin saber por qué ni quién lo cambió.
  - La plataforma ve si un negocio marca como falsas muchas de las solicitudes que recibe, y qué teléfonos molestan a varios.
  - Pedir y cancelar con otro teléfono, desde la misma cuenta, ya no reinicia el tope.
- **Lo que limita:**
  - **El correo llega aunque el cambio se revierta enseguida:** cada cambio manda el suyo.
  - **El tope por cuenta solo vale con sesión:** quien pide sin cuenta con otro teléfono sigue empezando de nuevo. Lo acotan el límite por IP y el tope por teléfono.
  - **Cuatro dígitos pueden repetirse** entre teléfonos distintos. La lista agrupa por el teléfono completo, así que dos filas con la misma terminación son dos teléfonos.
  - **La consola lee las citas en línea marcadas de todos los negocios** cada vez que se abre. Lo hace por canal y fecha de la marca, sin un índice propio. Con muchas marcas convendría uno filtrado (pide migración).
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - que el correo no se mande al invitar y que nombre a quien cambió los avisos;
  - los 90 días y los 20 primeros de cada lista;
  - mostrar cuatro dígitos del teléfono;
  - que el tope por cuenta use el mismo mensaje;
  - los textos.
- **Reversibilidad:** sin migración. Volver al código anterior deja de mandar el correo, quita la sección de la consola y vuelve a contar el tope solo por teléfono.
