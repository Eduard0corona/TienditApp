# ADR 0012: Días especiales, textos del horario en el servidor y horario real en WhatsApp

- Estado: Aceptada
- Fecha: 2026-10-11

## Contexto

Con el horario de la Fase 19 ([ADR 0011](0011-opening-hours.md)), el menú en línea dejó de recibir pedidos fuera de horario, pero el horario era el mismo todas las semanas. Para cerrar el 16 de noviembre, en Navidad, en vacaciones o por inventario, el dueño tenía que quitar su horario o apagar el menú, y acordarse de volver a ponerlo. El 24 de diciembre no había forma de cerrar a las 18:00.

Además:

- **Dos redacciones.** El servidor decidía si estaba abierto y el cliente lo ponía en palabras. Agregar los motivos ("por Navidad") en dos lenguajes (C# y TypeScript) los iba a separar.
- **WhatsApp no sabía el horario.** Las respuestas automáticas contestaban "¿a qué hora abren?" con la descripción que escribió el dueño, aunque el negocio tuviera su horario capturado y estuviera cerrado.
- **Cambio de horario.** Tijuana y Matamoros todavía cambian la hora en verano; comparar relojes de pared reabría un negocio durante la hora que se repite.

## Decisión

1. **Una tabla propia, una fila por fecha.**
   - `SpecialDays` (`SpecialDay`, `ITenantScoped`) guarda una fila por fecha del negocio. Las filas con el mismo `PeriodId` son un periodo: una regla sobre días seguidos.
   - La regla es "cerrado" u "otro horario" (uno o dos horarios; solo el último pasa de la medianoche y las 24 horas van solas), con un motivo opcional que ven los clientes: máximo 40 caracteres, en una sola línea.
   - Límites: un periodo dura de 1 a 92 días, empieza hoy o después (en la hora del negocio) y termina como mucho un año adelante (366 días). Un negocio tiene como máximo 120 fechas próximas.
   - El índice único `(TenantId, Date)` y cinco restricciones CHECK hacen imposible una fecha en dos periodos o una fila mal formada, aun si dos personas guardan a la vez. `Revision` es el token de concurrencia.
   - Se descartaron:
     - **Guardarlos dentro del JSON de `OpeningHours`:** la versión desplegada falla al leer otra forma después de un swap de regreso, la columna tiene 1,000 caracteres, y guardar la semana o "Quitar horario" borrarían los cierres.
     - **Una columna JSON en `TenantSettings`:** se leería en cada carga de la configuración, también en `GET /auth/my-tenant` al abrir la app; no tiene token de concurrencia y habría que podar el historial.
     - **`SettingsJson`:** una bolsa sin tipos.
     - **`OwnsMany`:** `ArchitectureTests` no revisa los tipos propios.
     - **Una fila por periodo con sus horarios en JSON:** la base no puede impedir rangos encimados, y un JSON que no se lee volvería un 500 el menú público.
2. **Precedencia.**
   - Un día especial reemplaza los horarios que empiezan en su fecha, siempre por encima del horario de la semana. La regla del ADR 0011 (un horario es del día en que empieza) no cambia: la noche anterior sigue pasando de la medianoche. Un miércoles 23 de 18:00 a 2:00 con el jueves 24 cerrado sigue abierto hasta las 2:00; para cortarla, el dueño marca también el 23 (el diálogo lo avisa solo cuando el horario del día anterior pasa de la medianoche).
   - Los días especiales aplican sin horario de la semana: un negocio sin horario puede marcar sus vacaciones. Para decidir, sus demás días cuentan como abiertos todo el día, y eso nunca se dice como horario: ni "Abierto las 24 horas" ni "cerramos a medianoche". El menú y WhatsApp solo muestran sus días especiales.
   - Sin horario de la semana ni días especiales nada cambia: el negocio nunca se ve cerrado (ADR 0011, punto 2).
3. **Instantes y horizontes.**
   - `BusinessSchedule` evalúa en instantes de la zona del negocio (`TenantDateTimeResolver`): una hora que se repite al atrasar el reloj cuenta la primera vez, y una que no existe al adelantarlo es el momento del salto. En Tijuana, un negocio que cierra a la 1:30 la noche del cambio no vuelve a abrir a la 1:15 del horario de invierno.
   - El horizonte corto sigue en ocho días: un negocio que no cierra en ese lapso dice "Abierto las 24 horas".
   - Para saber cuándo abre, busca hasta un año adelante: después de unas vacaciones largas, o en un negocio que abre solo los sábados con el sábado 26 cerrado, el menú dice la fecha de verdad ("abre el martes 5 de enero a las 9:00").
4. **El servidor escribe las palabras.** Esto reemplaza el punto 3 del ADR 0011 ("el cliente solo lo pone en palabras").
   - `BusinessHoursText` escribe la etiqueta del chip (`StatusLabel`: "Abierto", "Abierto las 24 horas" o "Cerrado", corta porque el chip no parte la línea) y su detalle (`StatusDetail`: "por Navidad · abre el sábado 26 a las 9:00", "cierra a las 18:00 por Nochebuena").
   - También escribe el aviso mientras no recibe pedidos (`ClosedNotice`), el del próximo día especial de la semana (`UpcomingNotice`: "El viernes 25 cerramos por Navidad."), el mensaje del pedido rechazado y las respuestas de WhatsApp. El menú, el pedido y WhatsApp leen el horario con el mismo lector (`BusinessHours.ReadAsync`), así que nunca se contradicen.
   - Las fechas dicen lo necesario: "hoy", "mañana", "el sábado 26", "el sábado 2 de enero" en otro mes, "el martes 5 de enero" desde una semana adelante y con el año después de 180 días. A 0 y 1 días, el mensaje del pedido rechazado es idéntico al de la Fase 19 ("Ahora está cerrado: abre mañana a las 9:00.").
   - El motivo sale solo cuando hoy es un día especial: un domingo de descanso antes de un lunes festivo no dice "por Día de la Revolución"; el aviso del próximo día especial explica el lunes.
   - `PublicOpeningHoursDto` solo suma campos con nulos; `todayDate` le dice al cliente que el servidor escribió las palabras. Sin él (una API anterior), el cliente usa su redacción de antes (`describeOpeningStatus`) durante una entrega.
5. **Un día especial cerrado también cierra las mesas.**
   - El punto 5 del ADR 0011 sigue fuera de horario, también cuando termina el horario especial (el 24 después de las 18:00), y el menú lo dice: "Cerrado · tu mesa sí puede pedir".
   - En un día especial cerrado, el pedido desde el QR de una mesa se rechaza como cualquier otro: ese día no hay quien atienda, y que la mesa esté activa (`DiningTables.IsActive`) es configuración, no que haya alguien sentado.
   - Es una **decisión por omisión**, tomada sin una preferencia expresa del dueño del producto, y vive en un solo método, `PublicOrderRules.TableOverridesClosure`, con su prueba. Las tarjetas de Horario y de Días especiales lo dicen.
6. **El historial no se borra.**
   - Las fechas antes de hoy no cambian. Un periodo pasado se ve en "Pasados (últimos 90 días)" y no se edita.
   - Un periodo en curso conserva su primer día; puede terminar desde ayer, y un cambio de regla pasa los días desde hoy a un periodo nuevo (los pasados conservan su id), así cada periodo tiene una sola regla.
   - Quitar borra un periodo futuro, acorta a ayer uno en curso y se rechaza para uno pasado.
   - Cada cambio deja una entrada en la bitácora por periodo (`SpecialDayAdded`, `SpecialDayUpdated`, `SpecialDayRemoved`, con el periodo antes y después), en el mismo guardado. Una petición que no cambia nada no guarda ni registra.
   - Todas las filas de un periodo desde hoy comparten `Revision`. Una edición con una versión vieja responde 409 (`concurrency_conflict`). Si otra persona marcó una de esas fechas al mismo tiempo, el caso de uso descarta lo suyo y relee: el mismo periodo es la respuesta (un doble toque), otro da "Otra persona marcó una de esas fechas al mismo tiempo. Recarga para verla.".
7. **Las sugerencias nunca se aplican solas.**
   - Mi negocio sugiere los festivos de los próximos 90 días que aún no son días especiales (`MexicanHolidays`, máximo 8).
   - Incluye los descansos obligatorios del artículo 74 de la Ley Federal del Trabajo y los días que muchos negocios cierran: Jueves y Viernes Santo, Día de Muertos, Virgen de Guadalupe, y Nochebuena y Fin de año con cierre a las 18:00, recortado del horario de ese día y conservando el descanso del mediodía.
   - Solo sugiere días que el negocio abriría. Una sugerencia abre el diálogo lleno y nada cambia hasta guardar, porque cerrar el menú tiene consecuencias y un toque equivocado no se deshace solo.
   - No hay días de elecciones ni festivos estatales.
8. **WhatsApp dice el horario de verdad.**
   - Las palabras de horario ("horario", "abren", "cierran", "abierto", "festivo", "vacaciones"…) contestan con el horario: si está abierto ahora y por qué no ("Estamos cerrados por Navidad: abrimos mañana a las 9:00."), la semana en tramos ("Lunes a sábado: 9:00 a 20:00") y los días especiales de las próximas dos semanas, máximo cuatro.
   - "A qué hora", "hora", "atiende", "abrir" y "cerrar" no cuentan, para que "¿A qué hora llega mi pedido?" no reciba el horario.
   - El orden es: bienvenida, horario, descripción (si preguntan dónde están o el negocio no tiene horario de la semana), productos, menú y al final las preguntas de datos. Mientras está cerrado, el menú en modo pedidos se ofrece solo para verlo.
   - Con horario de la semana, la descripción del negocio ya no contesta las preguntas de horario, salvo que también pregunten dónde están; el aviso bajo "Descripción del negocio" en Respuestas automáticas lo dice. Esto también es una decisión por omisión.
   - Si el horario no se puede leer, el log registra un error con el id del negocio (sin teléfono ni texto) y el cliente recibe la respuesta de antes.

## Consecuencias

- **Lo que logra:**
  - El dueño marca sus festivos, vacaciones e inventarios una vez, con sugerencias, y su menú y WhatsApp los dicen con su motivo ("Cerrado · por Navidad · abre el sábado 26 a las 9:00").
  - El 24 de diciembre cierra a las 18:00 sin tocar su semana.
  - Nadie pierde el historial y dos personas que editan a la vez no se pisan.
  - El cliente lee lo mismo en el menú, en el pedido rechazado y en WhatsApp, también la noche del cambio de horario.
- **Lo que limita:**
  - Los días especiales son del negocio, no de cada sucursal; los pedidos en línea siguen llegando a la principal.
  - No se repiten cada año: el 16 de noviembre cambia de fecha, y las sugerencias hacen que agregarlo sean dos toques.
  - No hay pedidos programados para cuando abra.
  - WhatsApp no contesta fechas puntuales ("¿abren el 25?") más allá de los días especiales de las próximas dos semanas. Su estado es el del momento en que procesa el mensaje, que puede ser minutos después si hay atraso.
  - El tope de 120 fechas se revisa antes de guardar: dos altas a la vez pueden pasarlo por poco. La integridad no cambia: el índice único y el límite de un año acotan cada lectura a unas 368 filas.
  - El catálogo de festivos puede quedar viejo (cambios pendientes al artículo 74). Vive en un archivo, y sus pruebas fijan 2026 a 2030.
  - Mientras dure la transición hay dos redacciones: la del servidor y la anterior del cliente, solo sin `todayDate`.
- **Zona horaria:** las fechas y el cambio de horario dependen de los datos de zonas horarias del servidor, y `TenantDateTimeResolver` cae en UTC sin avisar si faltan. `TimeZoneDataTests` cuida el servidor de CI, y la entrada `business-time-zones` de `/health/ready` cuida el App Service: queda Degraded si una zona que un negocio puede elegir cae en UTC, y la prueba de humo la exige Healthy ([observabilidad](../architecture/observability.md)).
- **Decisiones por omisión:** la mesa en un día especial cerrado y la descripción que ya no contesta el horario en WhatsApp se tomaron sin una preferencia expresa del dueño del producto; el PR pide confirmarlas.
- **Reversibilidad:**
  - La versión anterior no mapea la tabla: volver a ella no necesita tocar la base. Los días especiales se quedan guardados sin efecto (los menús reciben pedidos en las fechas cerradas) y vuelven a aplicar al publicar otra vez esta versión.
  - El `rollback.sql` borra la tabla y con ella los días especiales, también los pasados. La bitácora conserva cada cambio con sus valores, y la consulta 4 de su `verify.sql` cuenta lo que se perdería.
