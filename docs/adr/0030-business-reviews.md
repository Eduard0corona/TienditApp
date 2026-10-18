# ADR 0030: Reseñas de los negocios

- Estado: Aceptada
- Fecha: 2026-10-18

## Contexto

Las apps de entrega muestran la calificación de cada negocio en la tarjeta y en su tienda, y para quien no conoce un lugar es lo primero que lee. TienditApp ya tiene cuentas de cliente que compran y agendan (ADR 0022 y 0025), Explorar (ADR 0021) y la tienda con la forma de esas apps (ADR 0026). No tenía cómo decir qué tal es un negocio.

Una reseña es texto que escribe un cliente sobre un negocio y que ve cualquiera. Faltaban varias decisiones: quién puede escribirla, qué puede decir, cómo se ve, qué puede hacer el negocio con ella y quién decide cuando el negocio dice que no debería estar.

## Decisión

1. **Solo reseña quien compró o fue atendido.**
   - **Quién:** una cuenta de cliente activa (rol `Customer`) que recibió un pedido del negocio (entregado o completado) o asistió a una cita con él (completada). El equipo de un negocio no escribe reseñas, ni del suyo ni de otro.
   - **Una por cuenta y negocio:** el índice único `(UserId, TenantId)` lo garantiza en la base. Escribir otra vez cambia la misma; dos pestañas que la escriben a la vez dejan una, y la segunda responde que ya la había escrito.
   - **Qué lleva:** de 1 a 5 estrellas y un comentario opcional de hasta 500 caracteres.
   - **Qué no lleva:** enlaces, correos ni teléfonos (`BusinessReview.HasContactOrLink`). Una reseña habla del negocio; no es un anuncio ni una forma de contactar a alguien. La misma regla aplica a la respuesta del negocio y al nombre que se publica.
     - **Un teléfono** son diez dígitos o más, con espacios, guiones, puntos o paréntesis entre ellos: un número de México, con o sin `+52`.
     - **Una fecha** (`18-10-2026`) o un folio de ocho dígitos no son un teléfono y pasan.
   - **El autor:** la cambia o la borra cuando quiere. Una que la plataforma ocultó ya no se cambia, pero se puede borrar. Borrar la cuenta borra sus reseñas.
2. **Lo público dice poco de quien la escribió.**
   - **El nombre:** solo el nombre y la inicial del apellido («Mariana O.»). Se guarda al escribirla y se actualiza al cambiarla. Ninguna respuesta pública lleva su correo, su teléfono ni el id de su cuenta.
     - **«Cliente»** si la cuenta no tiene nombre, o si su nombre lleva un enlace, un correo o un teléfono: el nombre se publica con cada reseña y sigue la regla del comentario. Un apellido con eso se queda sin inicial.
     - **Un nombre largo** se corta por caracteres completos: un emoji nunca sale a la mitad, tampoco como inicial.
   - **El orden:** de la más nueva a la más antigua, diez por página, cada una con la respuesta del negocio y si se editó.
   - **La calificación:** el promedio, con un decimal, aparece desde tres reseñas publicadas en la tienda, en su tarjeta de Explorar y en Clientes › Reseñas. Una o dos reseñas no son un promedio:
     - la tienda y Clientes › Reseñas dicen cuántas hay;
     - la tarjeta de Explorar no dice nada, para no poner «1 reseña» junto a negocios con promedio.
   - **Explorar** la lee de su copia de 30 segundos (ADR 0021); la tienda la calcula en cada visita.
3. **El negocio responde y reporta, pero no borra.**
   - **Responder:** una vez por reseña. La respuesta se cambia o se quita, y se ve bajo la reseña. Una oculta no se responde.
   - **Reportar:** con un motivo de una lista (`ReviewConcern`: insultos, datos personales, publicidad, no habla de una compra o cita, otro), nunca con un texto, para que nadie escriba sobre el cliente. **Reportar no la oculta:** sigue publicada hasta que la plataforma decide. Una reseña que la plataforma ya revisó y mantuvo se reporta otra vez solo si su autor la cambió después.
   - **Quién:** el dueño o un administrador del negocio (`[RequiresTenantManager]`), en Clientes › Reseñas. Cada negocio ve y toca solo las suyas: las de otro no existen para él (404).
4. **La plataforma decide, y queda registrado.**
   - **Qué ve:** las reportadas, la del reporte más antiguo primero, y las ocultas (`[RequiresPlatformAdmin]`, en Plataforma › Reseñas).
   - **Qué hace:**
     - **Ocultar:** con un motivo de la misma lista. La reseña deja de verse y de contar en la calificación; su autor ve el motivo.
     - **Mantener:** el reporte se cierra y la reseña sigue.
     - **Restaurar:** una oculta vuelve a publicarse.
   - **El registro:** cada decisión queda en la actividad del negocio (`AuditLog`: `ReviewHidden`, `ReviewKept`, `ReviewRestored`), con el motivo y nada del texto ni del autor.
   - **Por qué la plataforma y no el negocio:** un negocio que pudiera ocultar las reseñas que no le gustan haría que todas valieran menos. Ninguna corrección borra lo que el cliente escribió; solo cambia si se ve.
5. **Las reseñas no van en los datos estructurados de la tienda.** Google no muestra estrellas de un negocio que publica las reseñas de sí mismo en su página («self-serving reviews»): agregarlas no sirve y puede contar en contra (ADR 0027).
6. **La base garantiza lo que el dominio decide.**
   - **Restricciones:** estrellas de 1 a 5; un reporte siempre con su motivo y solo en una reseña publicada; una oculta siempre con su motivo; una respuesta siempre con su fecha. Cada una dice `IS NOT NULL` donde importa: en SQL Server `NULL BETWEEN 1 AND 5` no es falso y pasaría.
   - **Al borrar:** un negocio con reseñas no se puede borrar (un negocio nunca se borra); una cuenta, sí, y se lleva sus reseñas.
   - **Los cambios de estado** pasan por el agregado (`BusinessReview`): `Write`, `Edit`, `ReplyWith`, `Report`, `Hide`, `Keep`, `Restore`.
   - **Dos cambios a la vez:** la columna `Version` (`rowversion`) hace que el segundo responda 409 («Alguien más modificó este registro al mismo tiempo») en lugar de pisar al primero.
     - **Un ejemplo:** el negocio reporta mientras la plataforma la oculta. El reporte responde 409 y la reseña se queda oculta con el motivo de la plataforma, sin un reporte escrito sobre lo que leyó antes.
     - **Lo mismo** para el autor que la cambia mientras el negocio responde o la plataforma decide.
     - **Una reseña que su autor borró** mientras la plataforma la revisaba responde 404.

## Consecuencias

- **Lo que logra:**
  - Un cliente ve qué tal es un negocio antes de comprar, en Explorar y en su tienda.
  - Cada reseña viene de alguien que compró o fue atendido ahí.
  - El negocio contesta en público y no puede callar a nadie.
  - La plataforma tiene una cola con lo que los negocios reportan y deja registro de lo que decide.
- **Lo que limita:**
  - **Sin respaldo de una compra concreta:** la reseña es del negocio, no de un pedido. Quien compró una vez hace un año puede reseñar hoy.
  - **Sin aviso:** el negocio no recibe un mensaje cuando llega una reseña, y la plataforma tampoco cuando hay un reporte; se ven al abrir sus pantallas.
  - **Sin fotos** en las reseñas, ni calificación por producto.
  - **Autorreseñas:** el dueño de un negocio puede abrir una cuenta de cliente con otro correo, comprarse a sí mismo y reseñarse.
    - **Qué lo frena hoy:** pide un pedido entregado o una cita completada, y solo una reseña por cuenta.
    - **Qué no se detecta:** que el correo o el teléfono de esa cuenta sea de alguien del equipo. Solo el negocio reporta, así que nadie más lo señala.
    - **Lo que falta:** que un cliente pueda reportar, o marcar para la plataforma las reseñas de cuentas que coinciden con el equipo.
  - **El filtro de contacto** reconoce enlaces, correos y números de diez dígitos o más. Un teléfono escrito con palabras («cinco cinco…») o partido en dos pasa; para eso está el reporte.
  - **El nombre del autor** se actualiza cuando cambia su reseña, no cuando cambia el nombre de su cuenta.
  - **Explorar** tarda hasta 30 segundos en mostrar una calificación nueva.
- **Privacidad:** lo público lleva el nombre corto del autor; ni su correo ni su teléfono ni su cuenta. El negocio ve lo mismo que el público, más el estado de cada reseña. Los motivos son de una lista para que ni el negocio ni la plataforma escriban sobre el cliente.
- **Reversibilidad:**
  - **Hay una migración** (`AddBusinessReviews`: la tabla `BusinessReviews` con su columna `Version`, sus índices y restricciones) con scripts idempotentes en `database/production/`.
  - **Su `Down` se detiene** (error 50023) en cuanto un cliente reseñó un negocio: revertir borraría lo que escribieron. Se corrige hacia adelante.
  - **Volver al código anterior** funciona sin tocar la base: no conoce la tabla, y la tienda, Explorar y las cuentas responden como antes.
