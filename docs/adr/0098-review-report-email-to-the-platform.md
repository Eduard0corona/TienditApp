# ADR 0098: Correo a la plataforma cuando un negocio reporta una reseña

- Estado: Aceptada en la Fase 32F (2026-11-17).
- Fecha: 2026-11-17
- Continúa: la [ADR 0030](0030-business-reviews.md) (reseñas, su reporte y la moderación de la plataforma) y la [ADR 0091](0091-online-order-reminder-and-review-emails.md) (el correo de cada reseña nueva, por el outbox).

## Contexto

- **Un reporte no se avisaba:** el negocio reporta una reseña con un motivo de la lista y la reseña sigue publicada hasta que la plataforma decide. Pero la plataforma solo se enteraba al abrir Plataforma › Reseñas. Mientras tanto, una reseña con datos personales o insultos seguía en la tienda.
- **Era lo último del punto 17 de la lista.** Faltaba decidir a quién se le manda: a todos los administradores de la plataforma o a una dirección configurada.

El dueño del producto pidió avanzar los puntos de la lista sin esperar su respuesta a cada decisión. Tomé las de esta fase con esa autorización. **No lleva migración.**

## Decisión

1. **Reportar avisa:** `BusinessReview.Report` levanta `BusinessReviewReportedEvent` (solo los ids de la reseña y del negocio). Un reporte que el dominio rechaza no avisa. Una reseña que la plataforma dejó, que su autor cambió y que el negocio reporta otra vez es un reporte nuevo y avisa otra vez.
2. **Le llega a cada administrador de la plataforma:** las cuentas con el rol `SuperAdmin`, activas y con el correo confirmado. Son las que la API deja entrar a Plataforma › Reseñas. No es una dirección configurada, porque:
   - no hay que mantener una dirección aparte ni un secreto de la app;
   - quien deja de ser administrador deja de recibirlos sin tocar la configuración;
   - una dirección configurada podría ser de alguien que no puede ocultar ni dejar la reseña.
3. **Un correo por administrador en el outbox** (`BusinessReviewReportEmailEvent`), que se manda y se reintenta solo. Antes de mandarse revisa otra vez lo que pudo cambiar desde que se formó:
   - que la reseña siga publicada y reportada, es decir, que nadie la haya ocultado, dejado o borrado;
   - que el reporte sea de hace un día o menos (después espera en la consola);
   - que la cuenta siga siendo administradora, activa y con el correo confirmado.
4. **Un negocio que reporta muchas a la vez:** de cada negocio se mandan los correos de los primeros 5 reportes dentro de una hora. Si antes de un reporte ya hay 5 pendientes de ese negocio en la hora anterior, ese reporte no forma correos: espera en la consola, que los lista todos. Un negocio no llena el correo de la plataforma reportando todas sus reseñas.
5. **Lo que dice:**
   - **Asunto:** «Reseña reportada en {negocio}: {motivo}».
   - **Cuerpo:**
     - el negocio y el nombre corto del autor, como la tienda lo muestra;
     - las estrellas y el motivo, con el mismo texto de la consola (`REVIEW_CONCERN_LABELS`);
     - el comentario como lo escribió, en una línea y codificado;
     - que sigue publicada mientras la revisa, y el enlace a `/platform/reviews`.
   - Nunca la cuenta ni el correo del autor.
   - No ofrece «dejar de recibirlos en Mi perfil», porque esa opción no existe para la plataforma.

## Consecuencias

- **Lo que logra:** la plataforma se entera de cada reporte sin abrir la consola y decide a tiempo sobre una reseña que sigue publicada.
- **Compatibilidad:** sin migración ni cambio de contrato. La versión anterior no conoce el evento. Si después de desplegar se vuelve a ella, un evento de reporte que quedó en el outbox sin correr no se puede leer, porque esa versión no conoce su tipo, y se aparta tras sus intentos. No cambia nada más.
- **Lo que limita:**
  - **Sin opción para dejar de recibirlos:** cada administrador recibe todos. Una preferencia por cuenta pediría una columna, es decir, una migración.
  - **Solo correo:** sin WhatsApp ni notificaciones del navegador, como los demás avisos.
  - **El tope por negocio cuenta los reportes pendientes:** uno que la plataforma ya decidió en esa hora deja de contar.
  - **Un reporte dejado, cambiado y reportado otra vez antes de que salgan los correos del primero** manda los dos con lo que la reseña dice ahora: el evento lleva solo ids.
- **Decisiones por omisión** (sin preferencia expresa del dueño):
  - a todos los administradores de la plataforma y no a una dirección configurada;
  - 5 correos por negocio en una hora;
  - un día para mandarlo.
