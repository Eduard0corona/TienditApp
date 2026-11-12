# ADR 0092: El chat muestra las fotos y los audios que mandan los clientes por WhatsApp

- Estado: Aceptada en la Fase 31L (2026-11-12).
- Fecha: 2026-11-12
- Continúa: el chat de WhatsApp de la Fase 4, que guardaba cada foto o audio como un marcador («📷 Foto», «🎤 Nota de voz»).

## Contexto

- **El chat guardaba las fotos y los audios como marcador:** el negocio veía que llegó una foto o una nota de voz, pero tenía que abrir WhatsApp en el teléfono para verla u oírla.
- **Estaba en la deuda técnica** (punto 18 de la lista): descargarlos pide guardar el archivo por negocio.

El dueño del producto pidió avanzar los puntos de la lista sin esperar su respuesta a cada decisión. Tomé las de la Fase 31L con esa autorización. **Lleva migración:** su `apply.sql` va antes del merge.

## Decisión

1. **Solo fotos y audios** (`image` y `audio` del webhook). Videos, documentos, stickers, ubicaciones y contactos siguen como marcador. El texto del mensaje sigue siendo su marcador, con el pie de foto si lo trae.
2. **El mensaje guarda qué trajo y en qué va** (`WhatsAppMessages`: `MediaKind`, `MediaId`, `MediaPath`, `MediaState` — pendiente, guardado o no disponible). Una restricción (`CK_WhatsAppMessages_Media`) mantiene cada fila como la escribe el dominio. El id de WhatsApp se acepta solo si es suyo (letras, números, punto, guion o guion bajo, hasta 128).
3. **Se descarga en segundo plano** por el outbox (`WhatsAppMediaReceivedEvent`, guardado con el mensaje en la misma transacción), con el WhatsApp del negocio:
   - primero la dirección del archivo (`GET /{media-id}` de Meta), luego el archivo; **el token solo va a los servidores de Meta** por https (`lookaside.fbsbx.com`, `*.fbsbx.com`, `*.fbcdn.net`);
   - **fotos hasta 5 MB y audios hasta 16 MB**, los límites de WhatsApp: uno más grande no se lee completo;
   - toda la descarga, el contenido incluido, tarda a lo más 2 minutos (`MediaDownloadTimeout`): los tiempos del cliente HTTP se detienen en los encabezados, y una descarga atorada detendría el outbox de esa instancia (correos, auditoría, facturas);
   - si Meta no contesta ahora (tiempo agotado, demasiadas peticiones, falla suya) el outbox lo intenta de nuevo; en el último intento cualquier falla lo da por no disponible (también un tiempo agotado del almacenamiento), menos el outbox que se detiene, para que el chat no espere para siempre.
4. **Lo que es lo dice su contenido,** nunca Meta ni el nombre:
   - una foto se lee y se guarda otra vez como WebP, sin metadatos (dónde se tomó, el teléfono) y con su lado más largo hasta 1,600 px, como las fotos de un servicio; a lo más dos a la vez por instancia;
   - un audio se guarda como llegó solo si un navegador lo reproduce (Ogg —las notas de voz de WhatsApp—, MP3, MP4/M4A o AAC); AMR y lo demás quedan como marcador.
5. **Se guarda privado, en la carpeta del negocio** (`whatsapp/{negocio}/{mes}/{guid}.{ext}`), y el mensaje guarda `/files/whatsapp/...`, que abre **quien maneja el chat de ese negocio** (`ManageWhatsApp`); el de otro negocio responde como uno que no existe.
6. **El chat** muestra la foto (se abre completa) o el reproductor del audio sobre el texto del mensaje, siempre con la sesión; mientras llega dice «Descargando la foto…» o «…el audio…» (un día a lo más: después queda el marcador), y se actualiza al momento (`MessageMedia` por SignalR). Solo pide con la sesión una ruta de las que la app crea para esos archivos (`/files/whatsapp/{negocio}/{aaaamm}/{guid}.{ext}`), diga lo que diga el mensaje.
7. **Deshacer la migración se detiene (error 50061) sin cambiar nada si ya se guardó una foto o un audio**: se perdería dónde está. La consulta 4 del `verify.sql` los cuenta. Los pendientes o no disponibles conservan su marcador.

## Consecuencias

- **Lo que logra:** el negocio ve la foto del pedido o del problema y oye la nota de voz sin salir de TienditApp.
- **Compatibilidad:** la versión desplegada no conoce las columnas y sigue guardando el marcador. Volver a ella funciona sin `rollback.sql`.
- **Lo que limita:**
  - **Sin limpieza:** los archivos se quedan mientras exista su mensaje; uno guardado justo antes de una falla de la base queda sin mensaje que lo nombre, como las fotos de un servicio.
  - **Lo de antes de la fase** sigue como marcador: no se descarga hacia atrás.
  - **Safari** reproduce Ogg/Opus solo desde versiones recientes; en uno que no, el reproductor lo dice y queda WhatsApp.
  - **WhatsApp guarda los archivos por un tiempo** (alrededor de 30 días): si el outbox tarda más, ya no están y quedan como marcador.
  - **El negocio que apaga su WhatsApp** deja de descargar: lo pendiente queda como marcador.
  - **Sin videos, documentos ni stickers:** pesan más o son otro formato; es lo siguiente natural.
- **Decisiones por omisión** (sin preferencia expresa del dueño): solo fotos y audios; los límites de WhatsApp; volver a codificar la foto y no el audio; detener el rollback en lugar de perder dónde están.
