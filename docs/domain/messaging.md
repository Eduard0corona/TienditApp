# Mensajería: campañas, recordatorios y bajas

Fase 2. Todo sale por WhatsApp Cloud API con la configuración de cada negocio y en segundo plano.

## Plantillas

- `MessageTemplate` guarda nombre, uso (`Campaign` o `AppointmentReminder`), texto con variables y, opcionalmente, el nombre de la plantilla aprobada en Meta (`ProviderTemplateName`) y su idioma (`es_MX` por defecto). Los avisos de citas (`AppointmentNotice`) no tienen plantilla: los escribe el sistema con la cita completa.
- Variables: `{nombre}` (primer nombre del cliente o "cliente") y `{negocio}`; los recordatorios suman `{servicio}`, `{fecha}` ("lunes 5 de octubre") y `{hora}` ("4:30 p. m."), en la zona horaria del negocio. Una variable que no aplica al uso se rechaza.
- WhatsApp solo entrega texto libre a quien escribió en las últimas 24 horas. Fuera de esa ventana hace falta una plantilla aprobada en Meta: el mensaje se envía como plantilla y las variables se mandan como parámetros en el orden en que aparecen en el texto (`{nombre}` → `{{1}}`…). Sin nombre de Meta se envía el texto; si Meta lo rechaza por la ventana (código 131047) el mensaje queda como fallido con esa explicación.
- Editar una plantilla no cambia los mensajes ya encolados: cada mensaje guarda su texto y parámetros.

## Campañas

- Audiencias: todos los clientes con teléfono (registrados y quienes compraron), compradores con al menos N compras completadas (opcionalmente en los últimos días) y clientes que dejaron de venir (sin compras en los últimos días). Los teléfonos se normalizan y el mismo número no se repite aunque esté guardado como `+52` o `+521`.
- La vista previa (`POST /api/v1/messaging/campaigns/preview`) cuenta destinatarios y excluidos por baja y muestra los primeros mensajes con el teléfono enmascarado.
- Una campaña se programa para ahora o hasta 90 días después. Al llegar su hora, el worker elige a los destinatarios en ese momento y encola un `OutgoingMessage` por cada uno en un solo guardado (todo o nada); la clave `campaign:{id}:{teléfono}` evita duplicados aunque dos instancias la inicien a la vez. Tope: 5,000 destinatarios por campaña.
- Cancelar una campaña la marca cancelada; el worker cancela sus mensajes pendientes antes de enviarlos. Los enviados se conservan.
- Una campaña termina cuando ninguno de sus mensajes queda pendiente. Su resultado se cuenta en SQL: enviados, entregados, leídos, fallidos, no enviados y cancelados.
- Pedidos de plataformas (Fase 22F, [ADR 0013](../adr/0013-delivery-platform-orders.md)):
  - Quien pide por Rappi, Uber Eats o DiDi Food es cliente de la plataforma: su pedido no guarda teléfono, así que nunca entra a una audiencia.
  - Como defensa, las tres audiencias dejan fuera los pedidos de esos canales (`CampaignAudienceResolver`). `CampaignAudienceSqlServerTests` lo prueba en SQL Server con un teléfono puesto a mano en un pedido de Uber Eats.

## Recordatorios de citas

- Configurables por negocio (`TenantSettings`): activados o no (apagados por defecto), horas de anticipación (1 a 72, 24 por defecto) y plantilla (o el texto predeterminado). Requieren el módulo de citas y WhatsApp conectado.
- Cada 5 minutos el worker encola el recordatorio de las citas agendadas o confirmadas que empiezan dentro de la ventana (y en más de 15 minutos). La clave incluye la hora de la cita: si se reagenda, se encola uno nuevo y el anterior se omite.
- Antes de enviar se revisa que la cita siga vigente, a la misma hora y en el futuro.

## Avisos de citas (Fase 6)

- Al agendar, reprogramar, confirmar o cancelar una cita se encola un `OutgoingMessage` con uso `AppointmentNotice` en el mismo guardado que el cambio (no hay aviso sin cambio ni cambio sin aviso) y se despierta al worker. Antes se enviaba dentro de la solicitud, sin reintentos (`AppointmentNotices` reemplaza a `AppointmentNotifier`).
- Solo se encola con WhatsApp conectado y un teléfono que se pueda normalizar. Los pasos internos (en servicio, completada, no asistió) y guardar el mismo estado ya no mandan mensaje.
- Cada aviso dice la cita completa: servicio, negocio, día y hora en la zona horaria del negocio ("Hola Luis, tu cita de Corte ahora es el martes 6 de octubre a las 10:00 a. m. en Café Luna."). Si al llegar su turno ya hay otro más reciente de la misma cita, se omite ("Hay un aviso más reciente de la misma cita"): un reintento no llega después con la hora vieja. Uno que esperó más de 24 horas (el worker estuvo detenido) también se omite.
- La baja no los detiene, igual que los avisos de pedidos: el cliente pidió esa cita. Son texto libre, así que WhatsApp solo los entrega a quien escribió en las últimas 24 horas; si no, quedan como fallidos con esa explicación.
- Clientes → Campañas → Recordatorios de citas muestra los últimos avisos y su resultado.

## Bajas

- Un mensaje entrante que es solo "BAJA", "STOP", "ALTO" o "darme de baja" registra la baja del número y se responde con una confirmación; "ALTA" o "START" la revierte. No se usa la respuesta automática para esos mensajes. Frases como "quiero cancelar mi pedido" no cuentan.
- El negocio también puede registrar o reactivar una baja a petición del cliente (permiso `ManageCampaigns` o `ManageCustomers`). Cada cambio se audita.
- La baja aplica a campañas y recordatorios y se revisa al enviar cada mensaje. Los avisos de pedidos (confirmación, cambios de estado) y de citas siguen llegando: el cliente los pidió al hacer el pedido o la cita, y la confirmación de la baja se lo dice. Qué pedidos los reciben lo decide la [política de avisos del negocio](order-lifecycles.md#avisos-al-cliente-fase-4): por defecto, ninguna venta de mostrador. Un pedido de Rappi, Uber Eats o DiDi Food nunca los recibe, con cualquier política (Fase 22F).

## Chat y respuestas automáticas (Fase 4)

- "Respuestas automáticas" (`AiAgentAutoReplyService`) reconoce palabras completas sin importar mayúsculas ni acentos (`AutoReplyKeywords`): "menú" coincide con "menu" y "hi" ya no coincide dentro de "chilaquiles". No hay modelo de lenguaje: contesta con los textos del negocio.
- Saludo: el mensaje de bienvenida y, solo ahí, los datos que pide (nombre si WhatsApp no lo da, correo, dirección). Precios, menú o pedido: los productos del negocio y el enlace a su menú en línea (`{AppUrl}/m/{slug}`) cuando está prendido; si no, invita a escribir lo que quiere para que el equipo tome el pedido. "Gracias", "ok" y similares reciben el mensaje de cierre.
- Horario (Fase 21, [ADR 0012](../adr/0012-special-days.md)):
  - Las palabras de horario ("horario", "abren", "abre", "abierto", "cierran", "cierra", "cerrado", "atienden", "hasta qué hora", "festivo", "vacaciones" y sus formas) contestan con el horario real, leído con el mismo lector que el menú y su pedido (`BusinessHours.ReadAsync`, en el alcance del negocio del mensaje). La respuesta (`BusinessHoursText.ForChat`) dice si está abierto ahora y por qué no ("Estamos cerrados por Navidad: abrimos mañana a las 9:00.", "Hoy tenemos horario especial por Nochebuena: cerramos a las 18:00."), la semana en tramos ("Lunes a sábado: 9:00 a 20:00", "Domingo: cerrado") y los días especiales de las próximas dos semanas desde hoy, máximo cuatro: el horario especial de hoy completo y uno en curso con hasta cuándo dura ("Hasta el sábado 7 de noviembre: 10:00 a 14:00 (Remodelación)"). Habla como el negocio, en plural, con la hora como la escribe el menú que abre su enlace ("9:00", no "9:00 a. m.").
  - No cuentan "a qué hora", "hora", "atiende", "abrir" ni "cerrar": "¿A qué hora llega mi pedido?", "¿Quién me atiende?" y "¿Me cierras la cuenta?" no preguntan el horario. Las de ubicación ("ubicación", "dirección", "dónde están", "cómo llego") siguen contestando con la descripción del negocio.
  - Orden de la respuesta: la bienvenida si saludan, el horario, la descripción si preguntan dónde están o si el negocio no tiene horario de la semana, los productos si preguntan por ellos, el enlace al menú y, al final, las preguntas de datos si saludaron. Con horario de la semana, la descripción ya no contesta sola una pregunta de horario.
  - Sin horario ni días especiales, o sin nada que decir, todo sigue como antes: contesta la descripción, si la hay, y un negocio sin horario ni días especiales nunca dice "cerrado". Sin horario de la semana no dice la semana ni "abierto las 24 horas": solo sus días especiales y, en uno de ellos, si está abierto o cerrado ("Estamos cerrados por Navidad: abrimos mañana.").
  - Mientras está cerrado, el menú en modo pedidos se ofrece solo para verlo ("Mira el menú aquí: …"), y la respuesta a precios o menú agrega cuándo abre ("Ahora estamos cerrados: abrimos mañana a las 9:00.").
  - `AutoReplyComposer` arma las respuestas del horario y del catálogo en 1,300 caracteres sin cortar nunca el texto del sistema (horario, enlace y preguntas): recorta el del negocio, así un texto de productos largo ya no corta el enlace del menú. Ningún corte parte un emoji.
  - Si el horario no se puede leer, el log registra un error ("Business hours for the automatic reply failed for tenant {TenantId}", sin teléfono ni texto) y el cliente recibe la respuesta de antes. El estado es el del momento en que se procesa el mensaje: con atraso en la bandeja puede ser de minutos antes, y cada mensaje recibe como máximo un intento de respuesta.
- No contestan cuando una persona del equipo escribió en el chat en las últimas 12 horas (`WhatsAppConversation.AutoRepliesPausedUntil`), cuando el chat las pausó a mano (un día, `PUT /whatsapp/chat/{id}/auto-replies`) ni a fotos, notas de voz y demás medios.
- Fotos, videos, notas de voz, documentos, stickers, ubicaciones (con enlace al mapa) y contactos se guardan como un marcador ("📷 Foto: …"); antes se descartaban. Las reacciones se ignoran.
- Cada mensaje enviado desde el chat y cada respuesta automática guarda su `wamid`, así que las palomitas avanzan (enviado, entregado, leído, no entregado) y el chat las recibe en vivo (`MessageStatus` en el hub).
- Un número mexicano llega de WhatsApp como `+521…` y se escribe como `+52…`: el chat, la ficha, las bajas y los pedidos lo tratan como el mismo (`MessagingPhone`). Una conversación iniciada desde el negocio recibe la respuesta del cliente en el mismo hilo.

## Cola y worker

- `OutgoingMessages` es la cola y el historial. `MessagingWorker` (Infrastructure, `BackgroundJobs/Messaging`) reclama mensajes con token de concurrencia y lease, envía cada uno en el alcance de su negocio (`BackgroundScopes.CreateTenantScope`: filtros, auditoría y servicios como en una solicitud de ese negocio) y pausa 100 ms entre envíos (unos 10 por segundo).
- Un solo cliente de la API de WhatsApp (`WhatsAppCloudClient`, Fase 6): los mensajes de cada negocio (`WhatsAppService`) y los códigos de verificación de la plataforma (`SmsService`) usan la misma versión de la API, el mismo armado de mensajes, la misma regla de qué fallos se reintentan y el mismo registro (sin el cuerpo de Meta fuera de Development y con el teléfono enmascarado). `WhatsAppService` solo envía con el WhatsApp del negocio en alcance: pedirlo para otro negocio responde "no configurado" sin usar su token.
- Fallos temporales (timeout, 429, 5xx, red) se reintentan con espera creciente hasta 6 intentos; un rechazo de Meta se marca como fallido. Los estados de entrega del webhook (entregado, leído, fallido) se aplican al mensaje por su `wamid`.
- Opciones en `BackgroundJobs:Messaging` (intervalo, lote, intentos, lease, pausa entre envíos, frecuencia de recordatorios). El health check marca degradado si hay mensajes pendientes con más de 15 minutos de atraso.
- `POST /api/v1/marketing/promotions/send` (anterior) se conserva para clientes viejos: ahora crea una campaña inmediata con la plantilla "Promoción rápida" y responde a cuántos clientes llegará.

## Pendiente

- Retención del historial de `OutgoingMessages` (hoy se conserva todo).
- Plantillas de Meta con encabezado, botones o multimedia; hoy solo cuerpo con parámetros de texto.
- Ventanas de envío por horario del negocio (no enviar de noche).
