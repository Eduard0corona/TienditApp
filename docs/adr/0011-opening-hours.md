# ADR 0011: Horario del negocio y pedidos fuera de horario

- Estado: Aceptada
- Fecha: 2026-10-10

## Contexto

El menú en línea recibía pedidos a cualquier hora mientras hubiera una caja abierta en la sucursal principal. Si alguien dejaba la caja abierta, una fonda que cierra a las 18:00 podía recibir un pedido para recoger a las 23:00, y nadie le decía al cliente cuándo abría. El menú tampoco mostraba el horario ni la dirección, que es lo primero que busca quien llega por un enlace o un QR.

## Decisión

1. **El horario es del negocio y vive en su configuración.**
   - `TenantSettings.OpeningHours` es un valor (`OpeningHours`): por día de la semana, hasta dos horarios, para un descanso a mediodía.
   - Un cierre igual o anterior a la apertura termina al día siguiente (18:00 a 2:00); la misma hora en los dos es abierto las 24 horas.
   - Solo el último horario de un día puede pasar de la medianoche, y no puede terminar después de que abre el día siguiente.
   - Se guarda como una lista JSON en una columna con nulos (`nvarchar(1000)`); al leerla, `OpeningHours.Create` revisa otra vez las reglas.
2. **Sin horario, nada cambia.** Un negocio que no captura horario nunca se ve cerrado, y su menú recibe pedidos como antes. La columna empieza vacía en todos.
3. **El servidor decide si está abierto, en la zona horaria del negocio.**
   - `OpeningHours.StatusAt` toma los horarios desde ayer hasta ocho días adelante y une los que se tocan, como un viernes que cierra a las 2:00 y un sábado que abre a esa hora.
   - Responde si está abierto y cuándo cierra, o cuándo abre.
   - El menú recibe el resultado (`PublicOpeningHoursDto`), y el cliente solo lo pone en palabras: "Abierto · cierra a las 20:00", "Cerrado · abre mañana a las 9:00".
4. **Fuera de horario, el menú se ve pero no recibe pedidos para recoger o a domicilio.**
   - `GET /api/v1/public/menu/{slug}` responde `acceptsOrders: false` y `ordersPaused: true`.
   - `POST /api/v1/public/menu/{slug}/orders` rechaza el pedido aunque haya una caja abierta, con "Ahora está cerrado: abre mañana a las 9:00.".
   - El servidor lo revisa en las dos rutas; ocultar el botón en el cliente no basta.
5. **Excepción: el cliente en una mesa.**
   - Un pedido desde el QR de una mesa llega a cualquier hora: el cliente ya está sentado, como en un pedido tomado en el mostrador, y el negocio decide si lo atiende.
   - Cualquiera puede escribir `?mesa=` en el enlace, así que solo cuenta una mesa del negocio que esté en uso (`DiningTables`, activa). Cualquier otra mesa es solo una nota del pedido y no se salta el horario.
   - El servidor lo revisa en el menú (`?table=`, que muestra los botones para pedir) y al recibir el pedido (`tableLabel`).
6. **Lo cambian dueños y administradores.**
   - La ruta es `PUT /api/v1/auth/my-tenant/opening-hours` (`[RequiresTenantManager]`), desde Configuración › Negocio › Horario.
   - Cada cambio queda en la bitácora (`OpeningHoursUpdated`) con el horario anterior y el nuevo.
   - Una lista vacía quita el horario.
7. **La dirección del menú es la de la sucursal principal, nunca la fiscal.**
   - El menú muestra dónde vende el negocio, con "Cómo llegar": la sucursal principal (`Branch.IsDefault`), la misma que recibe los pedidos en línea.
   - La dirección fiscal (`Tenant.Address`) se queda en las facturas. En un negocio chico suele ser la casa del dueño, y el menú es una página pública con un enlace al mapa.
   - Sin calle no se publica: una ciudad sola no le dice a nadie cómo llegar.
   - La primera versión de la Fase 19 publicaba la dirección fiscal; se cambió el mismo día, antes de que algún negocio la capturara para eso. Configuración avisa en las dos pantallas cuál se publica.

## Consecuencias

- **Lo que logra:**
  - El cliente ve si el negocio está abierto, su horario de la semana y cómo llegar.
  - El negocio deja de recibir pedidos para recoger cuando no hay quien los prepare, aunque una caja haya quedado abierta.
  - El negocio que no captura horario sigue igual.
  - Nadie publica su dirección fiscal sin saberlo.
- **Lo que limita:**
  - No hay días festivos ni cierres especiales (vacaciones, inventario): por ahora el dueño quita el horario o apaga el menú.
  - El horario es del negocio, no de cada sucursal; los pedidos en línea llegan a la sucursal principal.
  - No hay pedidos programados para cuando abra.
- **Decisión por omisión:** se tomó sin una preferencia expresa del dueño del producto. Si se prefiere que fuera de horario el menú reciba pedidos para prepararlos al abrir, el cambio está en `GetPublicMenuQueryHandler` y `PlacePublicOrderCommandHandler`, y sería un modo nuevo del negocio, no una regla para todos.
- **Reversibilidad:**
  - La versión anterior no mapea la columna: volver a ella no necesita tocar la base, y los menús vuelven a recibir pedidos a cualquier hora con una caja abierta.
  - El `rollback.sql` borra la columna y con ella los horarios capturados. La bitácora conserva cada cambio.
