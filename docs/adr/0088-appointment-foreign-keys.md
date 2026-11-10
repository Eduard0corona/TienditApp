# ADR 0088: Las llaves foráneas del servicio y de quien atiende una cita

- Estado: Aceptada en la Fase 31H (2026-11-10).
- Fecha: 2026-11-10
- Continúa: la Fase 22 (la agenda guarda el servicio del catálogo y a la persona del equipo) y la [ADR 0032](0032-online-booking-staff.md) (las citas en línea eligen con quién).

## Contexto

- **Una cita apunta a dos filas que la base no revisaba.** `Appointment.ServiceProductId` es el producto del catálogo que agenda (su nombre queda en `ServiceName`). `Appointment.StaffUserId` es la cuenta de quien la atiende (su nombre queda en `StaffName`). No tenían llave foránea: la aplicación revisa al guardar que el servicio y la persona sean del negocio (`AppointmentResources`), pero nada impedía un vínculo a algo que no existe.
- **Los productos se borran de verdad.** «Eliminar» en Catálogo borra la fila (`DeleteProductCommand`). Las citas de ese servicio se quedaban con un `ServiceProductId` que ya no apuntaba a nada.
- **Las cuentas no se borran.** No hay ningún camino que borre una fila de `AspNetUsers`: una persona que deja el equipo se desactiva.
- **La deuda técnica lo tenía pendiente** (punto 12 de la lista).

El dueño del producto pidió avanzar los puntos de la lista sin esperar su respuesta a cada decisión. Tomé las de la Fase 31H con esa autorización. **Lleva migración:** su `apply.sql` va antes del merge.

## Decisión

1. **`FK_Appointments_Products_ServiceProductId` con `ON DELETE SET NULL`.** Borrar un producto deja a sus citas con el nombre con que se agendaron y sin el vínculo. Es lo que una cita ya hace cuando no tiene servicio del catálogo (las anteriores a la Fase 22). No impide borrar el producto, que hoy se puede borrar aunque tenga citas o ventas.
2. **`FK_Appointments_AspNetUsers_StaffUserId` sin cascada**, como la de la cuenta del cliente (`CustomerUserId`). Como nadie borra cuentas, no cambia nada de lo que hace la app.
3. **Las dos llaves apuntan al `Id`, no al negocio y al `Id`.** Que el producto y la persona sean del negocio lo sigue revisando la aplicación al guardar.
   - Una llave compuesta (`TenantId`, `ServiceProductId`) no puede vaciar el vínculo al borrar el producto (`SET NULL` vaciaría también `TenantId`) y obligaría a un índice único más sobre `Products`.
   - Para la persona, la membresía (`TenantUsers`) se quita sin que eso deba tocar sus citas.
   - El `verify.sql` lista las citas con un servicio de otro negocio (se esperan cero).
4. **Índices filtrados** (`IX_Appointments_ServiceProductId`, `IX_Appointments_StaffUserId`, solo las citas con vínculo) sostienen las llaves: borrar un producto busca sus citas sin recorrer la tabla.
5. **La migración vacía primero los vínculos que no apuntan a nada:** el producto ya borrado y la cuenta que no existe. Cada cita conserva el nombre del servicio y de quien atiende. No pide decidir nada: un `Id` que no apunta a nada no tiene otro arreglo. El `verify.sql` los lista antes (consultas 3 y 4).
   - Bloquea `Appointments`, y deja leer pero no borrar `Products`, hasta crear las llaves, en la misma transacción. Así ningún producto que borre la versión desplegada mientras tanto deja un vínculo nuevo a nada.

Ningún endpoint, código HTTP ni mensaje cambia.

## Consecuencias

- **Lo que logra:** la base ya no guarda una cita con un servicio o una persona que no existe. Borrar un producto limpia el vínculo de sus citas en la misma sentencia, también desde la versión anterior.
- **Compatibilidad:** la versión desplegada (master con la Fase 31G) no conoce las llaves y sigue igual. Agenda solo con un servicio y una persona que encontró en el negocio; al borrar un producto, la base vacía el vínculo de sus citas. Volver a ella funciona sin `rollback.sql`.
- **Lo que limita:**
  - **Los vínculos vacíos no regresan** con el `rollback.sql`: apuntaban a algo que no existe.
  - **Una cita editada mientras se borra su producto** puede responder 409: el `SET NULL` cambia la fila (su `rowversion`), como cualquier otra edición a la vez.
  - **Que el producto y la persona sean del negocio** sigue en la aplicación, no en la base.
- **Decisión por omisión** (sin preferencia expresa del dueño): vaciar el vínculo al borrar el producto en lugar de impedir el borrado; vaciar los vínculos huérfanos en la migración en lugar de detenerla.
