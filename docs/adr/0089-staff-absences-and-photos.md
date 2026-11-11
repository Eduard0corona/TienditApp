# ADR 0089: Los días que no atiende quien atiende citas en línea, y su foto

- Estado: Aceptada en la Fase 31I (2026-11-11).
- Fecha: 2026-11-11
- Continúa: la [ADR 0032](0032-online-booking-staff.md) (las citas en línea eligen con quién) y la [ADR 0020](0020-home-visits.md) (la visita a domicilio usa el mismo calendario).

## Contexto

- **Quien atiende no tenía ausencias por fecha.** Una persona que falta unos días se apagaba en «Quién atiende» (y había que acordarse de encenderla), o el negocio marcaba un día especial, que cierra para todos.
- **Tampoco tenía foto.** La página de citas y la de la visita muestran su inicial.
- **Las dos estaban en la deuda técnica** de la Fase 24J (punto 15 de la lista). El anticipo de las citas, del mismo punto, espera la decisión del dueño del producto y no entra aquí.

El dueño del producto pidió avanzar los puntos de la lista sin esperar su respuesta a cada decisión. Tomé las de la Fase 31I con esa autorización. **Lleva migración:** su `apply.sql` va antes del merge.

## Decisión

1. **Una ausencia es una serie de días completos del calendario del negocio, los dos incluidos** (`OnlineBookingStaffAbsence`, del agregado `OnlineBookingStaff`; tabla `OnlineBookingStaffAbsences`, que se borra con la persona).
   - Hasta 20 por venir por persona y hasta 366 días cada una. Dos no se cruzan (días seguidos sí: son dos). Una que termina antes de empezar no se guarda; la base lo revisa también (`CK_OnlineBookingStaffAbsences_Days`).
   - Se guardan todas juntas con la persona (`PUT online-booking/staff/{userId}`, campo `absences`): reemplazan las que tenía y las que ya terminaron antes de hoy (del negocio) se quitan. Sin el campo (`null`) se quedan las que tiene: una pestaña abierta con la versión anterior no las borra.
   - El registro de actividad de la persona las incluye.
2. **Ningún horario que empieza en un día de su ausencia es de esa persona**, en la página de citas y en la de la visita, al mostrar las horas y al pedir la cita (`StaffSlots`, el mismo cálculo). Cuenta el día en que empieza, en el horario del negocio: en un negocio que abre de noche, las 23:30 del lunes son del lunes aunque la persona falte el martes. «Sin preferencia» toma a otra persona libre, o no ofrece la hora.
3. **Las citas que ya tiene no cambian, y la agenda del equipo no revisa las ausencias.** El formulario lo dice: «Sus citas de esos días no cambian; revísalas en tu agenda». El negocio decide qué hace con ellas.
4. **La foto que ven los clientes es del negocio, no de la cuenta** (`OnlineBookingStaff.PhotoUrl`).
   - El avatar de la cuenta lo elige la persona y lo ven todos sus negocios; esta la elige el dueño o un administrador para su página, con la capacidad de agenda, y otro negocio la muestra a su manera o no la muestra.
   - Se sube (`POST online-booking/staff/{userId}/photo`) y se quita (`DELETE`) al momento, aparte del resto del formulario, y solo para alguien ya guardado en «Quién atiende». Las mismas reglas que la portada: imagen de hasta 5 MB, con las variantes del logo (se decodifica al hacerlas).
   - Va a una carpeta pública del negocio, `staff/{negocio}`, como la portada. Los archivos de la anterior se borran después de guardar la nueva, y solo si están en esa carpeta; si el almacenamiento falla ahí, el cambio ya está guardado y responde bien, y el registro lo dice. Subir y quitar quedan en la actividad.
   - Las páginas muestran la variante más chica (`_xs`, 128 px) en un círculo; sin foto, la inicial, como antes.
5. **Deshacer la migración se detiene (error 50059) sin cambiar nada si algún negocio ya puso una ausencia o una foto**: se perderían. La consulta 3 del `verify.sql` las cuenta por negocio y dice cómo borrarlas si de todos modos se decide revertir.
6. **`sqlcmd -t 15` al correr el `apply.sql` y el `rollback.sql`.** Crear o borrar la tabla toma un candado de esquema sobre `Tenants` y `OnlineBookingStaff` hasta el commit (comprobado con `sys.dm_tran_locks`); mientras lo espera y lo tiene, esperan las solicitudes que leen los negocios.

## Consecuencias

- **Lo que logra:** una persona de vacaciones deja de ofrecerse solo esos días, sin apagarla ni cerrar el negocio, y vuelve sola. Los clientes la reconocen por su foto.
- **Compatibilidad:** la versión desplegada (master con la Fase 31H) no conoce la tabla ni la columna; sigue guardando y borrando a quien atiende. Volver a ella funciona sin `rollback.sql`: la página vuelve a ofrecer a cada persona también en sus ausencias y deja de mostrar su foto; lo guardado se queda.
- **Lo que limita:**
  - **Solo días completos:** una tarde libre se resuelve con su horario propio o apagándola.
  - **La página no dice por qué** alguien no tiene horas un día (no dice «de vacaciones»).
  - **La agenda del equipo no revisa las ausencias**, y las citas ya agendadas de esos días no se mueven ni avisan.
  - **La foto es pública**, como el logo y la portada: la ve quien abra la página de citas. El formulario pide subirla con permiso de la persona.
  - **Archivos sin uso en `staff/{negocio}`**, como con la portada: los de una foto cuya persona se borra (al borrar su cuenta, que hoy ningún camino hace), los que subió una foto que no se pudo guardar, los de dos fotos subidas a la vez para la misma persona y los de una anterior que el almacenamiento no dejó borrar (queda en el registro). No hay limpieza.
  - **Dos dueños que cambian a la misma persona al mismo tiempo:** gana el último que guarda, también con sus ausencias, como con el resto de su perfil.
  - **Mientras se crea la tabla** (milisegundos con las tablas libres, hasta 15 s si espera) las solicitudes que leen los negocios esperan. Mejor en una hora tranquila.
- **Decisiones por omisión** (sin preferencia expresa del dueño): días completos en lugar de horas; el día en que empieza el horario decide; las citas ya agendadas no cambian; la foto separada del avatar y solo para alguien guardado; detener el rollback en lugar de perder lo guardado.
