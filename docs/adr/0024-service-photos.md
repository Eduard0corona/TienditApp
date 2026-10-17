# ADR 0024: Fotos del servicio

- Estado: Aceptada
- Fecha: 2026-10-17

## Contexto

Una orden de servicio (lavanderías, talleres, reparaciones) guarda fotos del antes y del después desde la Fase 4C: dos listas JSON en su detalle (`ServiceOrderDetail.BeforePhotosJson` y `AfterPhotosJson`, hasta 20 en cada una), privadas desde la Fase 8A. Pero:

- solo se adjuntaban al crear la orden en «Recibir servicio con piezas» (`/orders/cart`);
- Pedidos y el tablero de servicios solo las mostraban: ninguna ruta agregaba una foto a una orden existente, así que las del después, que se toman al terminar o al entregar, no tenían dónde capturarse;
- una foto no decía quién la tomó ni cuándo.

Las decisiones del dueño para la Fase 25:

- las fotos van por orden, con una nota opcional en cada una;
- el cliente no las recibe todavía;
- se guardan mientras exista la orden, y las de una orden entregada no se borran.

## Decisión

1. **Una fila por foto** (`ServiceOrderPhoto`, tabla `ServiceOrderPhotos`).
   - Guarda su etapa (antes o después, con una CHECK), el archivo privado (`/files/{key}`), su nombre, una nota opcional de hasta 300 caracteres («Mancha en la manga»), cuándo se tomó (el reloj del servidor) y quién (el nombre de la sesión y su cuenta).
   - Es del negocio (`ITenantScoped`, con su filtro) y de la orden: llave a `Orders` con borrado en cascada.
   - Va por orden, no por pieza: la nota dice qué prenda o qué parte muestra.
2. **El agregado decide** (`Order.AddServicePhoto` y `RemoveServicePhoto`).
   - Solo una orden de servicio lleva fotos. Una cancelada ya no recibe más; una entregada o terminada sí, la de la entrega.
   - Hasta 20 fotos agregadas en cada etapa. Las de la recepción siguen aparte, con su propio límite.
   - Una foto se quita solo mientras la orden está abierta. Entregada, terminada o cancelada, sus fotos se conservan.
   - Cada cambio marca la fila de la orden, así que su `rowversion` atrapa dos fotos agregadas a la vez: la segunda se intenta otra vez con lo que guardó la primera (hasta tres veces), y el límite se respeta. Lo mismo con una orden que se cierra mientras alguien quita una foto.
3. **Quién.**
   - Ver, agregar y quitar fotos: `ManageOrders` o `ManageServiceBoard`, con el módulo de servicios. Son los mismos que abren el archivo de una foto (Fase 8A). La subida (`POST /orders/uploads/service-photo`) también se abre a `ManageServiceBoard`: el taller toma la foto donde trabaja.
   - Quitar una foto: quien la tomó (por su cuenta) o un dueño o administrador del negocio. La evidencia que tomó una persona no la quita otra.
   - El archivo tiene que ser uno subido desde la app para el mismo negocio y en su área (`service-intake`). Otra dirección, el archivo de otro negocio o el de un comprobante de pago se rechazan, como al recibir la orden.
4. **Qué se ve** (`GET /orders/{id}/service-photos`).
   - Primero, las fotos de la recepción, las de las listas JSON: sin nota ni autor, y no se quitan.
   - Después, las agregadas, en el orden en que se tomaron, cada una con su nota, quién y cuándo, y si quien pregunta puede quitarla.
   - Pedidos y el detalle de una pieza en el tablero muestran el panel `ServicePhotosPanel`: antes y después; agregar una foto con la cámara del teléfono, con su etapa y su nota; y quitarla con confirmación. En el tablero se ve aunque quien lo trabaja no pueda ver el resto del pedido.
5. **Auditoría.** Cada foto agregada o quitada entra a la actividad de la orden por el outbox (`ServiceOrderPhotoAdded` y `ServiceOrderPhotoRemoved`): la foto, su etapa, su archivo, quién y cuándo, sin la nota.
6. **El cliente no las recibe:** ni por WhatsApp, ni en el ticket, ni en el seguimiento ([ADR 0023](0023-order-tracking.md)).

## Consecuencias

- **Lo que logra:**
  - El después se captura al terminar o al entregar, desde el mostrador o desde el taller.
  - Cada foto agregada dice quién la tomó y cuándo, y lo que muestra.
  - La evidencia de una orden entregada se conserva.
- **Lo que limita:**
  - Las fotos van por orden: una lavandería con diez prendas distingue cada una por la nota.
  - El archivo de una foto quitada se queda en el almacenamiento privado, sin que nada lo use. Tampoco se borra una foto que se subió y no se agregó. No hay limpieza.
  - Las fotos de la recepción siguen en las listas JSON, sin autor; no se pasaron a la tabla.
  - Se guardan mientras exista la orden: no hay plazo ni borrado programado.
  - Sin tiempo real: otra pantalla abierta ve la foto nueva al volver a abrir el detalle.
  - El cliente no las ve: si un día las recibe, es otra decisión.
- **Reversibilidad:**
  - Volver a la versión anterior de la app no necesita la base: no conoce la tabla, y las fotos agregadas quedan guardadas sin mostrarse hasta volver a la nueva.
  - `rollback.sql` de `20261017183622_AddServiceOrderPhotos` quita la tabla solo si está vacía. En cuanto un negocio agregó una foto, se detiene con el error 50018 sin cambiar nada: borrarla perdería la evidencia, y solo queda corregir hacia adelante.
