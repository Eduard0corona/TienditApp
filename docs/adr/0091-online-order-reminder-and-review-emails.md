# ADR 0091: Recordatorio del pedido en línea que espera y correo de reseñas nuevas

- Estado: Aceptada en la Fase 31K (2026-11-12).
- Fecha: 2026-11-12
- Continúa: la [ADR 0078](0078-online-order-waiting-notice.md) (el aviso en pantalla del pedido que lleva 15 minutos), la [ADR 0082](0082-order-cancellation-email-reminder-lead-and-online-orders-index.md) (el correo de los pedidos en línea cancelados) y la [ADR 0030](0030-business-reviews.md) (reseñas).

## Contexto

- **El pedido del menú en línea que nadie acepta se avisaba solo en pantalla** (la campana, desde la 29X): quien no tenía TienditApp abierto no se enteraba, y a la media hora cuenta entre los más lentos en el tiempo de confirmar que ven los clientes en Explorar.
- **Una reseña nueva no se avisaba:** el negocio la veía solo si entraba a Reseñas.
- **Estaban en la deuda técnica** (punto 17 de la lista; el aviso de los reportes a la plataforma espera la decisión del dueño).

El dueño del producto pidió avanzar los puntos de la lista sin esperar su respuesta a cada decisión. Tomé las de la Fase 31K con esa autorización. **Lleva migración:** su `apply.sql` va antes del merge.

## Decisión

1. **Recordatorio por correo del pedido en línea que espera,** una vez por pedido:
   - De los que cuenta la campana: del menú en línea, «Por confirmar», pedidos por su cliente (no tomados por el equipo) y no de una mesa.
   - **Entre los 15 minutos** en que la campana lo marca y **la media hora** en que cuenta entre los más lentos. Pasada la media hora ya no se manda: la campana lo sigue marcando.
   - Lo recorre el worker de mensajes en cada pasada de recordatorios (cada 5 minutos por omisión), como el recordatorio de la Agenda: lee qué negocios tienen alguno, entre los activos que venden, y en el ámbito de cada uno el pedido decide si toca (`Order.RemindToConfirm`). Queda marcado (`Orders.ConfirmReminderSentAt`) y otra pasada, o el negocio que lo responde al mismo tiempo, no lo repiten (gana la versión).
   - **Le llega a quien recibe los correos de los pedidos en línea** (`TenantUser.OrderCancellationEmails`, de la 30B): los dueños hasta que eligen, y quien confirma pedidos y lo elige. La opción se llama ahora «Pedidos en línea» en Mi perfil y en Equipo, como la de la Agenda junta las solicitudes, su recordatorio y las cancelaciones.
   - Un correo por persona en el outbox, que antes de mandarse revisa que el pedido siga esperando y que la persona siga recibiéndolos y pueda confirmar pedidos.
   - Dice quién pidió, el folio y el total, que lleva más de 15 minutos y lo de la media hora, con el enlace a Pedidos. Nunca el teléfono, la dirección ni lo que pidió.
2. **Correo de cada reseña nueva:**
   - Escribir una reseña (`BusinessReview.Write`) lo avisa; editarla o responderla no. Una cuenta tiene una reseña por negocio, pero puede borrarla y escribirla otra vez, y cada vez que la escribe es nueva: por eso escribir, editar y borrar la reseña propia tiene un límite (`PUT` y `DELETE /api/v1/me/reviews/{slug}`): **10 por hora por cuenta** (`MyReviewsAccountLimit`, un filtro que corre ya con la sesión leída, porque el limitador de la app corre antes y solo distingue direcciones) y 60 por hora por dirección (política `my-reviews`, alta para quienes comparten una: el Wi-Fi de un food park, la red de un operador). Borrar y escribir en bucle no llena el correo del equipo, ni cambiando de red.
   - **Le llega a sus dueños y administradores,** que son quienes leen y responden las reseñas, con una opción propia (`TenantUser.ReviewEmails`): los dueños hasta que eligen. Se elige en Mi perfil (`GET` y `PUT /api/v1/reviews/emails`) y quien administra el equipo la elige por los demás, menos por el dueño, con el aviso de la 30A.
   - Un correo por persona en el outbox, que revisa que la reseña siga publicada (ni borrada ni oculta), que se escribió hace un día o menos, y que la persona siga recibiéndolos y sea dueña o administradora.
   - Dice las estrellas, el comentario como lo escribió (en una línea y codificado) y el nombre corto con que la tienda muestra al autor, con el enlace a Reseñas. Nunca su cuenta ni su correo.
3. **La migración agrega dos columnas nulas** (`Orders.ConfirmReminderSentAt` y `TenantUsers.ReviewEmails`) sin cambiar datos. Agregar una columna nula es solo de metadatos, pero toma un candado de esquema breve sobre `Orders` y `TenantUsers`: mejor con `sqlcmd -t 15`.
4. **Deshacerla no se detiene:** como las opciones de la 30B, olvida quién eligió los correos de reseñas y qué pedidos se recordaron; nada que haya escrito un cliente.

## Consecuencias

- **Lo que logra:** el negocio se entera por correo del pedido que espera antes de que cuente como lento, y de cada reseña nueva para responderla a tiempo.
- **Compatibilidad:** la versión desplegada no conoce las columnas y sigue igual (no manda ninguno de los dos correos). Volver a ella funciona sin `rollback.sql`.
- **Lo que limita:**
  - **Si el worker no pasa entre los 15 y los 30 minutos** (detenido o desplegándose), ese pedido no se recuerda.
  - **La lectura de qué negocios tienen alguno** recorre los pedidos sin un índice por estado: se lee con el índice de `TenantId` y `Status`, cada pasada. Un índice filtrado pide otra migración y la 30B lo midió más lento para otras lecturas.
  - **Quien quiere los cancelados pero no el recordatorio** no lo puede separar: es una sola opción.
  - **Una reseña editada** (de 5 a 1 estrella, por ejemplo) no se avisa otra vez; tampoco los reportes, que esperan la decisión del dueño.
  - **Borrar y escribir de nuevo** avisa otra vez, hasta el límite de la cuenta (10 por hora): el borrado no deja rastro con qué reconocer que es la misma reseña. Los dos límites son por instancia, como los demás: con varias, cada una cuenta los suyos.
  - **Sin WhatsApp ni notificaciones del navegador:** solo correo, como los demás avisos.
- **Decisiones por omisión** (sin preferencia expresa del dueño): reutilizar la opción de los pedidos en línea en lugar de una nueva; recordar solo hasta la media hora; avisar cada reseña escrita (no las ediciones); los dueños reciben los dos hasta que eligen.
