# Observabilidad

## Estado actual

### Health checks

| Endpoint | Qué revisa | Uso |
| --- | --- | --- |
| `GET /health/live` | Nada: responde 200 mientras el proceso corre. | Reinicio del proceso. |
| `GET /health/ready` | Conexión a la base (`database`), colas en segundo plano (`background-queues`), zonas horarias de los negocios (`business-time-zones`, Fase 21) y proveedor de correo (`email-provider`, Fase 23A). | Health check de App Service, balanceador, alertas y prueba de humo. |
| `GET /health/schema` | Que la base tenga todas las migraciones de esta versión (`database-schema`). | Prueba de humo del despliegue (Fase 7). |

- Los tres son anónimos y solo devuelven estados y el commit de la versión que responde (`{"status":"Healthy","checks":{...},"commit":"<sha>"}`), sin mensajes de error ni datos. El commit sale de la versión del ensamblado (`BuildInfo`): el SDK le agrega el de git, y el despliegue fija el que publica. La prueba de humo lo compara para saber qué versión contestó; un build sin commit responde `null`.
- `database-schema` queda **Unhealthy** (503) cuando falta en la base una migración de esta versión: producción no migra al arrancar y alguien publicó antes de aplicar su `apply.sql`. El log nombra la migración que falta. No forma parte de `/health/ready` a propósito: un esquema atrasado hace fallar el despliegue (o detiene el swap del slot), no saca de rotación a todas las instancias.
- `background-queues` queda **Degraded** (sigue respondiendo 200) cuando el outbox o la bandeja del webhook apartaron trabajo en las últimas 24 horas, o cuando hay trabajo pendiente con más de 15 minutos de retraso (workers detenidos o atorados).
- `business-time-zones` (Fase 21, `BusinessTimeZonesHealthCheck`) queda **Degraded** (sigue respondiendo 200) cuando una zona que un negocio puede elegir (`BusinessTimeZones.Supported`) cae en UTC en ese servidor, y también si el chequeo mismo falla: las zonas nunca sacan una instancia de rotación. Sin los datos de zonas horarias del sistema, `TenantDateTimeResolver` usa UTC sin avisar, y el horario, los días especiales y las fechas de esos negocios se correrían de cinco a ocho horas, sin el cambio de horario de Tijuana y Matamoros. No usa la base. El log nombra las zonas que faltan. `TimeZoneDataTests` revisa lo mismo, pero solo en el servidor de CI; este chequeo lo revisa donde corre la app, y la prueba de humo del despliegue exige `"business-time-zones":"Healthy"` en `/health/ready`, así que un servidor sin esos datos hace fallar el despliegue (con el slot de staging, la versión no llega a producción) en lugar de pasar sin que nadie lo note.
- `email-provider` (Fase 23A, `EmailProviderHealthCheck`) queda **Degraded** (sigue respondiendo 200) mientras no hay proveedor de correo, sus ajustes están incompletos o son los de ejemplo de `appsettings.json` (`YOUR_SMTP_USERNAME`, un remitente `.local`): nadie que se registre puede activar su cuenta, y no salen la recuperación de contraseña ni las invitaciones por correo. Lee los ajustes como `EmailService` (`EmailDeliverySettings`) y no se conecta al proveedor, así que unas credenciales equivocadas solo aparecen en el log de cada envío. El log dice qué falta, sin valores. La prueba de humo avisa sin fallar.
- Sin base de datos, `/health/ready` responde 503.
- En App Service: Configuración → Health check → ruta `/health/ready`.

### Telemetría (OpenTelemetry)

Se exporta solo si hay destino configurado:

- `APPLICATIONINSIGHTS_CONNECTION_STRING`: distro de Azure Monitor (trazas y métricas de ASP.NET Core, HttpClient y SQL Client, más el meter `TienditApp`).
- `OTEL_EXPORTER_OTLP_ENDPOINT` (y las variables estándar `OTEL_*`): exportador OTLP hacia cualquier colector, con las mismas fuentes y métricas de runtime.

Sin ninguno de los dos no se registra OpenTelemetry. Los logs salen por Serilog a la consola (log stream de App Service) y, con destino, Serilog también se los entrega al proveedor de logs de OpenTelemetry (`writeToProviders`), así llegan junto con trazas y métricas (Fase 4). Los proveedores de logging predeterminados se quitan para no duplicar la consola.

Nivel de los errores de las peticiones (Fase 10): `GlobalExceptionHandlerMiddleware` registra los rechazos esperados (400 de validación, 401, 403, 404, 409 de concurrencia y 422 de regla de negocio) como advertencia, sin la traza de pila ("Request rejected with {StatusCode} {ErrorType}"), y solo los 500 como error con la excepción ("Unhandled exception"). Una alerta sobre logs de nivel error mide entonces fallas de la app, no datos inválidos; la de 5xx por negocio de abajo usa `requests` y no cambia. El envío de códigos por SMS que piden las pantallas sin sesión corre en el outbox desde la Fase 11 (`VerificationCodeRequestedHandler`): un fallo del proveedor aparece como el de cualquier evento ("Outbox handler {Handler} failed", advertencia con reintento; tras 8 intentos, error "was set aside" y la métrica `tienditapp.outbox.messages` con `dead_lettered`), sin teléfono ni código; una solicitud que llegó tarde (más de 10 minutos) es la advertencia "A verification code asked for at ... was not sent: too late". Desde la Fase 23A los correos de las cuentas (activar la cuenta, restablecer la contraseña, "ya tienes una cuenta" e invitación al equipo) salen igual del outbox (`AccountEmailRequestedHandler`): una falla pasajera del SMTP (4xx, sin conexión) lanza `EmailDeliveryException`, sin la dirección ni la respuesta del servidor, y se reintenta; un rechazo definitivo (5xx) queda como el error "SMTP send failed ... Rejected for good: not retried." y no se reintenta; sin proveedor, la advertencia "Email not sent to ...: <qué falta>"; uno pedido hace más de un día, "An account email (...) asked for at ... was not sent: too late". La dirección sale enmascarada fuera de Development. Una petición que su cliente abandonó (una lectura que se detuvo, Fase 11) se registra como Information "Request stopped: its client disconnected" con el código 499, fuera de los errores y de la alerta de 5xx.

Respuestas automáticas de WhatsApp (Fase 21): si el horario de una respuesta no se puede leer, se registra como error "Business hours for the automatic reply failed for tenant {TenantId}" (con la excepción, sin teléfono ni texto) y el cliente recibe la respuesta de antes del horario; cuenta en las alertas de errores.

### Etiqueta por negocio

`TenantResolutionMiddleware` agrega `tenant.id` (GUID interno, no es dato personal):

- a la traza de la petición (`Activity`);
- a la métrica integrada `http.server.request.duration` (`IHttpMetricsTagsFeature`), junto con ruta y código de estado.

`RequestTelemetryEnrichmentMiddleware` (después de resolver el negocio) etiqueta además la traza con `tienditapp.tenant_id` y `enduser.id` y agrega `TenantId` y `UserId` (solo identificadores, nunca nombre, correo ni teléfono) a cada log escrito durante la petición.

### Métricas propias (meter `TienditApp`)

| Métrica | Etiquetas | Significado |
| --- | --- | --- |
| `tienditapp.outbox.messages` | `outcome` (`processed`, `failed`, `dead_lettered`), `event.type`, `tenant.id` | Efectos durables de eventos. |
| `tienditapp.inbox.deliveries` | `outcome`, `source` | Entregas del webhook. |
| `tienditapp.messages.sent` | `channel`, `outcome` (`sent`, `transient_failure`, `rejected`), `tenant.id` | Mensajes entregados al proveedor. |

## Alertas sugeridas por negocio

Consultas de ejemplo para Application Insights (Log Analytics). Ajusta umbrales y ventanas a tu volumen.

Errores 5xx por negocio (más de 5 en 10 minutos):

```kusto
requests
| where timestamp > ago(10m) and toint(resultCode) >= 500
| extend tenant = tostring(customDimensions["tenant.id"])
| summarize errors = count() by tenant
| where errors > 5
```

Mensajes de WhatsApp fallidos por negocio:

```kusto
customMetrics
| where timestamp > ago(30m) and name == "tienditapp.messages.sent"
| extend outcome = tostring(customDimensions["outcome"]), tenant = tostring(customDimensions["tenant.id"])
| where outcome != "sent"
| summarize failures = sum(valueSum) by tenant, outcome
```

Trabajo apartado en el outbox (cualquier ocurrencia):

```kusto
customMetrics
| where timestamp > ago(15m) and name == "tienditapp.outbox.messages"
| where tostring(customDimensions["outcome"]) == "dead_lettered"
| summarize total = sum(valueSum) by tenant = tostring(customDimensions["tenant.id"]), event = tostring(customDimensions["event.type"])
```

Disponibilidad: una prueba de disponibilidad (o el health check de App Service) sobre `/health/ready`.

## Deuda

- No hay paneles ni reglas de alerta versionados; las consultas de arriba son el punto de partida.
- Los logs de los workers (outbox, mensajería, reportes) no llevan todavía el negocio como propiedad; las peticiones HTTP sí.
- Los mensajes apartados (dead-lettered) se revisan con SQL sobre `OutboxMessages` e `InboxMessages`; no hay pantalla para reintentarlos.
