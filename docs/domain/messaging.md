# Mensajería: campañas, recordatorios y bajas

Fase 2. Todo sale por WhatsApp Cloud API con la configuración de cada negocio y en segundo plano.

## Plantillas

- `MessageTemplate` guarda nombre, uso (`Campaign` o `AppointmentReminder`), texto con variables y, opcionalmente, el nombre de la plantilla aprobada en Meta (`ProviderTemplateName`) y su idioma (`es_MX` por defecto).
- Variables: `{nombre}` (primer nombre del cliente o "cliente") y `{negocio}`; los recordatorios suman `{servicio}`, `{fecha}` ("lunes 5 de octubre") y `{hora}` ("4:30 p. m."), en la zona horaria del negocio. Una variable que no aplica al uso se rechaza.
- WhatsApp solo entrega texto libre a quien escribió en las últimas 24 horas. Fuera de esa ventana hace falta una plantilla aprobada en Meta: el mensaje se envía como plantilla y las variables se mandan como parámetros en el orden en que aparecen en el texto (`{nombre}` → `{{1}}`…). Sin nombre de Meta se envía el texto; si Meta lo rechaza por la ventana (código 131047) el mensaje queda como fallido con esa explicación.
- Editar una plantilla no cambia los mensajes ya encolados: cada mensaje guarda su texto y parámetros.

## Campañas

- Audiencias: todos los clientes con teléfono (registrados y quienes compraron), compradores con al menos N compras completadas (opcionalmente en los últimos días) y clientes que dejaron de venir (sin compras en los últimos días). Los teléfonos se normalizan y el mismo número no se repite aunque esté guardado como `+52` o `+521`.
- La vista previa (`POST /api/v1/messaging/campaigns/preview`) cuenta destinatarios y excluidos por baja y muestra los primeros mensajes con el teléfono enmascarado.
- Una campaña se programa para ahora o hasta 90 días después. Al llegar su hora, el worker elige a los destinatarios en ese momento y encola un `OutgoingMessage` por cada uno en un solo guardado (todo o nada); la clave `campaign:{id}:{teléfono}` evita duplicados aunque dos instancias la inicien a la vez. Tope: 5,000 destinatarios por campaña.
- Cancelar una campaña la marca cancelada; el worker cancela sus mensajes pendientes antes de enviarlos. Los enviados se conservan.
- Una campaña termina cuando ninguno de sus mensajes queda pendiente. Su resultado se cuenta en SQL: enviados, entregados, leídos, fallidos, no enviados y cancelados.

## Recordatorios de citas

- Configurables por negocio (`TenantSettings`): activados o no (apagados por defecto), horas de anticipación (1 a 72, 24 por defecto) y plantilla (o el texto predeterminado). Requieren el módulo de citas y WhatsApp conectado.
- Cada 5 minutos el worker encola el recordatorio de las citas agendadas o confirmadas que empiezan dentro de la ventana (y en más de 15 minutos). La clave incluye la hora de la cita: si se reagenda, se encola uno nuevo y el anterior se omite.
- Antes de enviar se revisa que la cita siga vigente, a la misma hora y en el futuro.

## Bajas

- Un mensaje entrante que es solo "BAJA", "STOP", "ALTO" o "darme de baja" registra la baja del número y se responde con una confirmación; "ALTA" o "START" la revierte. No se usa la respuesta automática para esos mensajes. Frases como "quiero cancelar mi pedido" no cuentan.
- El negocio también puede registrar o reactivar una baja a petición del cliente (permiso `ManageCampaigns` o `ManageCustomers`). Cada cambio se audita.
- La baja aplica a campañas y recordatorios y se revisa al enviar cada mensaje. Los avisos de pedidos (confirmación, cambios de estado) siguen llegando: el cliente los pidió al hacer el pedido. Qué pedidos los reciben lo decide la [política de avisos del negocio](order-lifecycles.md#avisos-al-cliente-fase-4): por defecto, ninguna venta de mostrador.

## Cola y worker

- `OutgoingMessages` es la cola y el historial. `MessagingWorker` (Infrastructure, `BackgroundJobs/Messaging`) reclama mensajes con token de concurrencia y lease, envía cada uno en el contexto de su negocio y pausa 100 ms entre envíos (unos 10 por segundo).
- Fallos temporales (timeout, 429, 5xx, red) se reintentan con espera creciente hasta 6 intentos; un rechazo de Meta se marca como fallido. Los estados de entrega del webhook (entregado, leído, fallido) se aplican al mensaje por su `wamid`.
- Opciones en `BackgroundJobs:Messaging` (intervalo, lote, intentos, lease, pausa entre envíos, frecuencia de recordatorios). El health check marca degradado si hay mensajes pendientes con más de 15 minutos de atraso.
- `POST /api/v1/marketing/promotions/send` (anterior) se conserva para clientes viejos: ahora crea una campaña inmediata con la plantilla "Promoción rápida" y responde a cuántos clientes llegará.

## Pendiente

- Retención del historial de `OutgoingMessages` (hoy se conserva todo).
- Plantillas de Meta con encabezado, botones o multimedia; hoy solo cuerpo con parámetros de texto.
- Ventanas de envío por horario del negocio (no enviar de noche).
