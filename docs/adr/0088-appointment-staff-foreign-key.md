# ADR 0088: La llave foránea de quien atiende una cita, y por qué el servicio no la lleva

- Estado: Aceptada en la Fase 31H (2026-11-10).
- Fecha: 2026-11-10
- Continúa: la Fase 22 (la agenda guarda el servicio del catálogo y a la persona del equipo) y la [ADR 0032](0032-online-booking-staff.md) (las citas en línea eligen con quién).

## Contexto

- **Una cita apunta a dos filas que la base no revisaba.**
  - `Appointment.StaffUserId` es la cuenta de quien la atiende; su nombre queda en `StaffName`.
  - `Appointment.ServiceProductId` es el producto del catálogo que agenda; su nombre queda en `ServiceName`.
  - Ninguno tenía llave foránea. La aplicación revisa al guardar que la persona y el servicio sean del negocio (`AppointmentResources`).
- **Las cuentas no se borran:** una persona que deja el equipo se desactiva. Ningún camino borra una fila de `AspNetUsers`.
- **Los productos sí se borran de verdad** (`DeleteProductCommand`), y el historial conserva su `Id`, como `OrderItem.ProductId`, que tampoco tiene llave. La cotización de una visita lo usa:
  - `QuoteVisitCredits` cruza las líneas de la venta de la visita con el `ServiceProductId` de la cita para saber cuánto se pagó del servicio y descontarlo («Se descuenta si contratas»).
  - Explorar cuenta la visita que nunca se cotizó comparando esas mismas líneas.
  - Los dos cruces funcionan aunque el producto ya no exista, porque los dos lados guardan el mismo `Id`.
- **La deuda técnica lo tenía pendiente** (punto 12 de la lista).

El dueño del producto pidió avanzar los puntos de la lista sin esperar su respuesta a cada decisión. Tomé las de la Fase 31H con esa autorización. **Lleva migración:** su `apply.sql` va antes del merge.

## Decisión

1. **`FK_Appointments_AspNetUsers_StaffUserId`, sin cascada**, como la de la cuenta del cliente (`CustomerUserId`). La sostiene un índice filtrado (`IX_Appointments_StaffUserId`, solo las citas con persona). Como nadie borra cuentas, no cambia lo que hace la app: la base deja de aceptar una cuenta que no existe.
2. **La migración se detiene (error 50058) sin cambiar nada si una cita ya apunta a una cuenta que no existe.**
   - Como las cuentas no se borran, ese `Id` sería de otra cosa, y conviene revisarlo en lugar de vaciarlo en silencio.
   - La consulta 3 del `verify.sql` las lista y dice cómo vaciar una: la cita conserva el nombre y la agenda la cruza por él.
   - Bloquea `Appointments` desde la revisión hasta crear la llave, en la misma transacción. La llave se crea revisando las filas (`WITH CHECK`), así que una cita guardada mientras tanto también la detendría (error 547); el candado hace que se detenga con este error y su mensaje.
3. **`ServiceProductId` se queda sin llave, a propósito.**
   - Una llave con `ON DELETE SET NULL` vaciaría el vínculo al borrar el producto. También obligaría a vaciar los que ya apuntan a productos borrados. En los dos casos el crédito de la visita dejaría de encontrar lo pagado: la cotización pública, su envío y su conversión descontarían 0 en lugar de lo cobrado. Explorar dejaría de contar esa visita como nunca cotizada. Nada de eso se puede devolver.
   - Una llave sin cascada impediría borrar un servicio que tiene citas. Hoy se puede borrar aunque tenga citas o ventas.
   - Queda como `OrderItem.ProductId`: un `Id` de historial, revisado contra el negocio al guardar.
4. **`sqlcmd -t 15` al correr el `apply.sql` y el `rollback.sql`.**
   - Crear o borrar la llave toma un candado de esquema sobre `AspNetUsers` hasta el commit (comprobado con `sys.dm_tran_locks`). Mientras lo espera y lo tiene, nadie lee las cuentas: todas las solicitudes con sesión revisan la suya ahí, así que esperan.
   - Con `-t 15`, un lote que tarda más (porque espera ese candado) se cancela; `XACT_ABORT` deshace su transacción sin cambiar nada y el script se vuelve a correr.
   - El bloque `SET` de los scripts se queda como el del job `Migrations`, que lo compara.

Ningún endpoint, código HTTP ni mensaje cambia.

## Consecuencias

- **Lo que logra:** la base ya no guarda una cita con una persona cuya cuenta no existe. Una cuenta con citas no se puede borrar, como ya pasaba con la del cliente.
- **Compatibilidad:** la versión desplegada (master con la Fase 31G) no conoce la llave y sigue igual. Solo guarda a una persona activa del equipo del negocio, con su cuenta, y nada borra cuentas. Volver a ella funciona sin `rollback.sql`.
- **Lo que limita:**
  - **Mientras se crea la llave** (milisegundos o pocos segundos con la tabla libre, hasta 15 s si espera), todas las solicitudes con sesión esperan y las citas no se guardan. Mejor en una hora tranquila.
  - **Que la persona sea del equipo del negocio** sigue en la aplicación, no en la base. Una persona que dejó el equipo sigue en sus citas de antes (la consulta 4 del `verify.sql` lo informa).
  - **`ServiceProductId` puede apuntar a un producto borrado:** es el historial, y lo leen el crédito de la visita y Explorar.
- **Decisión por omisión** (sin preferencia expresa del dueño): detener la migración ante una cuenta que no existe en lugar de vaciarla; no poner llave al servicio.
