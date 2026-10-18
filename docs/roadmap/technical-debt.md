# Deuda técnica

Actualizada el 2026-10-18 con la Fase 24D-2 y antes con la 24E, la 24D-1, la 25, la 24C, la 24B, la 24A, la 23A y con las Fases 22F, 22F-2, 22G, 22A, 22B, 22C, 22D y 22E, después de las Fases 0 a 21 del roadmap de producto (la auditoría y sus correcciones son del 2026-10-01).

## Crítica antes de producción

1. Credenciales expuestas: cadenas de conexión de Azure SQL y el secreto JWT estuvieron versionados y siguen en el historial de git. El código ya no los contiene y la API rechaza el secreto publicado, pero hay que rotarlos, revisar el firewall de Azure SQL y verificar si la cuenta SUPER_ADMIN sembrada con la contraseña anterior existe en la base remota. Reescribir el historial es destructivo y requiere coordinación.
2. Aislamiento (resuelto en la Fase 8 salvo lo indicado): toda entidad con `TenantId` implementa `ITenantScoped` y tiene el filtro del negocio (`ArchitectureTests` lo exige); desde la Fase 8 también `TenantSettings`, `WhatsAppConfig`, `OrderItem` y `OrderStatusHistory`, estas dos con el negocio de su orden (las que agrega la versión anterior mientras se publica la nueva se completan al arrancar, `OrderTenantBackfillService`). Los usos de `IgnoreQueryFilters()` están inventariados por archivo y motivo (`TenantFilterBypassAllowlistTests` rompe CI con uno nuevo) y los workers y el webhook trabajan en el alcance de cada negocio (`BackgroundScopes`, Fase 6). Resuelto en la Fase 10: el override `X-Tenant-Id` de `SUPER_ADMIN` queda en la actividad (`AuditLogs`) del negocio en el que actúa antes de ejecutar la petición; si no se puede registrar, responde 503 y no se ejecuta. Pendiente: cada uso de `IgnoreQueryFilters()` que queda depende de su predicado explícito (la suite cubre desde la Fase 13 los de las rutas de la cuenta), y el override no pide justificación ni tiene límites.
3. Suite de aislamiento (Fases 8 a 13): la API completa corre contra SQL Server 2022 con dos negocios registrados por la API (`TwoBusinessesApi`, compartida por las cuatro clases).
   - `CrossTenantHttpSqlServerTests`: uno no lee, cambia ni borra catálogo, clientes, proveedores, órdenes (estado y pagos), archivos privados, inventario (insumos, almacenes, movimientos y kárdex), compras, cajas ni citas del otro, ni con su encabezado `X-Tenant-Id`, su ruta de órdenes o el cambio de negocio, y las existencias del otro no se mueven. Desde la Fase 12 tampoco reembolsa ni anula sus órdenes, ni arma una receta con sus productos, insumos o almacenes, y el rechazo no deja filas guardadas. Desde la Fase 13 las rutas de la cuenta (`/me`) solo muestran los negocios, el acceso y las compras propios, y la lista de todos los negocios y crear uno responden 403 a un dueño.
   - `CrossTenantRealtimeSqlServerTests` (SignalR por WebSockets): los eventos de pedidos y de chat llegan solo al negocio dueño, y una conexión se cierra cuando termina su sesión mientras la de otra sesión sigue. Cada entrega firmada del webhook de WhatsApp llega solo al negocio de su número; sin firma no se guarda, un número sin negocio no llega a ninguno y nadie reclama el número de otro.
   - `CrossTenantManagementSqlServerTests` (Fase 10): mesas y cuentas abiertas (entre dos negocios de comida), sucursales, equipo (sin reenviar la invitación de otro), campañas (plantillas, campañas, bajas y audiencias, que nunca incluyen a los clientes del otro) y reportes (la venta de uno no mueve los del otro); desde la Fase 12, la promoción rápida, que llega solo a los compradores del negocio y no toca la plantilla del otro. Un admin de plataforma real deja en la actividad del negocio lo que hace en él (`CrossTenantHttpSqlServerTests`).
   - `CrossTenantOperationsSqlServerTests` (Fase 11): Inicio (las métricas y "Requiere atención" de uno no cuentan nada del otro), cocina (entre dos negocios de comida), tablero de servicios y agentes de IA.

   Desde la Fase 13 `CrossTenantSuiteCoverageTests` exige que cada controller con datos del negocio aparezca en la suite; quedan fuera, con su motivo, la consola de plataforma, el menú público (por slug), el 2FA de la cuenta y, desde la Fase 14, la recuperación de contraseña y el perfil, que son de la cuenta. Al escribirla encontró dos que la Fase 12 daba por cubiertos por coincidir solo en el prefijo de la ruta: `/api/v1/me` y `/api/v1/tenants`. Desde la Fase 14 cuenta cada URL por la ruta de cada acción, porque varios controllers comparten `/api/v1/auth` y `/api/v1/auth/my-tenant`, y así apareció un tercero: la configuración del negocio (`TenantSetupController`: asistente, plan, política de venta y menú en línea) solo contaba por compartir el prefijo de rutas que respondía `AuthController`; ahora `CrossTenantHttpSqlServerTests` prueba que con el encabezado de otro negocio un dueño solo lee y cambia su propia configuración. Pendiente: la prueba exige que la suite llame las rutas de cada controller, no que pruebe cada acción.
4. Archivos: el contenido se valida por firma, las fotos se re-codifican y el borrado por URL está confinado a `{área}/{tenantId}` en almacenamiento local y Azure Blob (rechaza traversal, otros tenants/áreas y URLs ajenas al contenedor). Desde la Fase 8 los comprobantes de transferencia y las fotos de servicio son privados: fuera de `wwwroot` o en un contenedor sin lectura anónima, solo los entrega `GET /api/v1/files/{key}` a miembros del negocio con el permiso del área, `/uploads/orders` responde 404 y los archivos que eran públicos se mueven al almacenamiento privado al arrancar ([seguridad](../../SECURITY.md#archivos)). Desde la Fase 9 la subida valida carpeta y nombre en los dos providers. Los avatares van a la carpeta de la cuenta (`avatars/users/{cuenta}`), y los anteriores se borran de la carpeta del negocio donde se subieron (por las membresías de la cuenta) o de la de sesión sin negocio. Pendiente:
   - los PDF no pasan por un escaneo de malware;
   - correr en producción el [procedimiento para las fotos de producto con la dirección de otro negocio](../../database/production/data-repair/20261005_StorageUrlsOutsideTheirFolder/README.md) (creadas antes del 2026-10-01);
   - un avatar que un `SUPER_ADMIN` subió mientras actuaba en otro negocio queda en la carpeta de ese negocio y no se borra al cambiarlo.
5. Webhook WhatsApp (actualizado 2026-10-02): la firma `X-Hub-Signature-256` se verifica fail-closed (503 sin `WhatsApp:AppSecret`, 401 sin firma válida; `WhatsApp:AllowUnsignedWebhooks` solo se respeta en Development), `PhoneNumberId` y `ExternalMessageId` tienen índices únicos filtrados (migración `AddWhatsAppWebhookUniqueIndexes`, verificada en SQL Server LocalDB) y las entregas repetidas o concurrentes son idempotentes; si dos primeros mensajes de un número nuevo llegan a la vez, el índice único `(TenantId, CustomerPhone)` deja crear una sola conversación y el otro mensaje se guarda en ella. Lo mismo ocurre si el negocio escribe a un número nuevo mientras llega su primer mensaje, o si dos envíos del negocio coinciden: `POST /api/v1/whatsapp/chat/send` guarda el mensaje, ya entregado, en la conversación ganadora de su tenant en lugar de responder 500 (verificado en SQL Server LocalDB con y sin `READ_COMMITTED_SNAPSHOT`). `WhatsAppConversation` tiene token de concurrencia `rowversion` (migración `AddWhatsAppConversationVersion`, verificada en SQL Server LocalDB con y sin `READ_COMMITTED_SNAPSHOT`; se aplica antes de desplegar el código que la usa): mensajes simultáneos del mismo cliente, auto-respuestas, envíos del negocio y "marcar leído" recargan la conversación y reaplican su cambio, hasta 10 intentos, en lugar de sobrescribir `UnreadCount` y el último mensaje. Cada mensaje del payload se procesa por separado y el `POST` responde 500 si alguno, o la actualización de estados, no se pudo procesar, para que Meta reenvíe la entrega (hasta 7 días); los mensajes ya guardados se ignoran por `ExternalMessageId`. JSON inválido y `PhoneNumberId` desconocido se confirman con 200. Como Meta no firma un timestamp, el replay se mitiga con esa idempotencia. Pendiente: un mensaje que falle siempre se reintenta y registra error durante esos 7 días; el envío del negocio llama a Meta antes de guardar y no hay Outbox, así que si el guardado falla por otra causa el mensaje entregado queda fuera del historial y la API responde 500; si una conversación recibe más de 10 escrituras simultáneas, la que agota los intentos falla (el webhook responde 500 y Meta reenvía, pero un envío del negocio o una auto-respuesta ya entregados quedan fuera del historial); `AppSecret` es global, así que un tenant con app de Meta propia (flujo que hoy sugiere la UI) será rechazado hasta definir secretos por tenant; la propiedad del `PhoneNumberId` no se verifica contra Meta (un admin podría reclamar primero el ID de otro negocio). Desde la Fase 9 la suite entre negocios prueba el webhook de punta a punta contra SQL Server: firma, bandeja, enrutamiento por número y reclamo de un número ajeno.
6. Resuelto en la Fase 4F: AutoMapper 13.0.1 (advisory alto `GHSA-rvv3-g6hj-g44x`, corregido solo en versiones con licencia comercial) se reemplazó por mapeo manual. Antes de quitarlo, una prueba temporal comparó cada mapeo nuevo con el de AutoMapper sobre entidades con todos sus miembros llenos; las listas proyectan en SQL con expresiones probadas contra SQL Server 2022. `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (`GHSA-2m69-gcr7-jv3q`) sigue sin versión corregida, pero solo lo usan las pruebas con SQLite.

## Alta

- Movimientos manuales de inventario registrados desde la pantalla antes de la corrección del 2026-10-04: se guardaron con el tipo y el motivo siguientes al elegido. Movieron las existencias en sentido contrario, y las "Salidas" guardadas como `Entry` promediaron su costo como si fueran compras. Falta revisarlos con cada tenant y compensarlos ([procedimiento y consulta de solo lectura](../../database/production/data-repair/20261004_InventoryMovementEnumOffset/README.md)). El dominio no tiene una operación para revaluar el costo promedio.
- Resuelto en la Fase 5: la autorización vive solo en cada caso de uso. El middleware resuelve el negocio y la membresía (toda ruta `/api` es del negocio salvo las de la cuenta), los controllers no tienen roles y los handlers ya no comparan el rol del token ni revalidan el módulo que declara su solicitud.
- Resuelto en la Fase 9: el valor por defecto de `UserRole` es `None`, que no da permisos y no se puede guardar. `SuperAdmin` se sigue guardando como 0 (`UserRoleStorage`), así que no hubo migración de datos; `RoleAssignmentGuardTests` exige que todo request con `UserRole` rechace `SuperAdmin` y `None`.
- Sesiones (resuelto en la Fase 8 salvo lo indicado): los refresh tokens se guardan solo como hash, rotan por sesión y un token reutilizado termina su sesión; los access tokens dejan de valer al cerrar la sesión, al cambiar o recuperar la contraseña y al cambiar el security stamp ([seguridad](../../SECURITY.md#autenticación-y-autorización)). Resuelto en la Fase 9: el login responde y tarda igual para un correo sin cuenta, una cuenta inactiva o bloqueada y una contraseña incorrecta; una conexión SignalR se cierra cuando termina su sesión; y los tokens ya no usan `RefreshTokens.Token` ni `ReplacedByToken` (la migración `StopUsingPlainRefreshTokenValues` convirtió a hash lo que quedaba en texto). Resuelto en la Fase 10: restablecer la contraseña por SMS responde lo mismo para una cuenta inexistente, inactiva o sin teléfono confirmado, un código incorrecto o vencido y una cuenta bloqueada; pedir el código (recuperación y reenvío de la confirmación) ya no espera al proveedor, que lo envía después de responder; y la migración `DropPlainRefreshTokenColumns` borró esas dos columnas y su índice. Resuelto en la Fase 11: pedir el código hace el mismo trabajo exista o no la cuenta (guarda la solicitud en el outbox sin buscarla) y un reinicio ya no pierde los códigos pendientes. Pendiente:
   - usar un código por SMS todavía tarda una escritura más en la base cuando la cuenta existe (el intento fallido que cuenta para el bloqueo); lo acota el límite de peticiones;
   - los códigos dependen de los workers del outbox (`BackgroundJobs`), y el correo de una solicitud de recuperación queda en el outbox hasta su limpieza (7 días).
- Resuelto en la Fase 11: la recepción de compra acepta una llave de idempotencia (`idempotencyKey`) que el cliente manda en cada intento; la misma recepción enviada dos veces, también a la vez, se registra una vez. Pendiente: un cliente anterior, sin llave, puede registrarla dos veces. (Los folios de órdenes, movimientos de inventario y órdenes de compra ya salen de un contador por negocio y no chocan: Fase 4F; los comandos que crean esos documentos corren en una transacción y devuelven el folio si se rechazan: Fase 4G.)
- Resuelto en la Fase 4F ([ADR 0008](../adr/0008-api-error-contract.md)): todo error es `ProblemDetails` con `code` estable y el cliente decide por él. Desde la Fase 5 también los errores de enlace de modelos responden en español (`SpanishModelBinding`: campo obligatorio, número mal escrito o JSON que no se lee, sin los mensajes de `System.Text.Json` ni nombres de tipos de .NET), las reglas de FluentValidation sin mensaje propio usan su traducción al español y el último mensaje de dominio en inglés quedó traducido. Los nombres de campo de esos mensajes son los de la API (`Phone`).
- Resuelto en la Fase 11: los controllers reciben el `CancellationToken` y lo pasan a su caso de uso; `RequestCancellation` decide su efecto: una lectura (GET) se detiene si el cliente se va y un cambio termina siempre, porque la mayoría de los casos de uso guarda en varios pasos fuera de una transacción. Pendiente: cancelar un cambio exigiría que cada caso de uso guarde de una vez o en una transacción.
- Resuelto en la Fase 9: las contraseñas nuevas tienen al menos 8 caracteres con mayúscula, minúscula y dígito, y se rechazan las comunes y las que contienen el correo o el nombre. Pendiente: las actuales valen hasta que se cambien y no se consulta una lista de contraseñas filtradas.
- Resuelto en la Fase 10: `GlobalExceptionHandlerMiddleware` registra los rechazos esperados (400, 401, 403, 404, 409 y 422) como advertencia, sin la traza de pila; solo los 500 quedan como error con la excepción.
- Activación por correo (2026-10-06): el registro de negocio y de cliente ya no abre sesión ni manda código por SMS; la cuenta se activa con el enlace del correo y después inicia sesión ([identidad](../architecture/identity-permissions-capabilities.md)). Resuelto en la Fase 23A: el registro responde lo mismo si el correo ya tiene cuenta (su dueño recibe el aviso por correo), y los correos de las cuentas salen del outbox con reintentos, el de la activación guardado con la cuenta. Pendiente:
   - sin un proveedor de correo configurado (`EmailSettings`) nadie que se registre puede activar su cuenta. Desde la Fase 23A los valores de ejemplo de `appsettings.json` cuentan como no configurados (no se intenta enviar con ellos), `/health/ready` lo dice (`email-provider`, Degraded) y la prueba de humo avisa. En producción ya está configurado y los correos llegan;
   - el registro de cliente todavía responde «Ya existe una cuenta de cliente con este teléfono» cuando una cuenta confirmó ese teléfono (antes de buscar el correo, así que no dice nada del correo); crear una cuenta escribe más filas que no crearla, una diferencia de milisegundos que la respuesta puede mostrar (probar un correo libre así lo registra, así que cada correo se prueba una vez); y dos registros simultáneos con el mismo correo pueden dar al segundo el error de Identity «Ya existe una cuenta con ese correo»;
   - un correo con cuenta puede recibir el aviso cada vez que alguien se registra con él: lo acota el límite de peticiones por IP, sin límite por cuenta.

## Media

- Explorar (Fase 24A, [ADR 0021](../adr/0021-business-directory.md)):
  - La foto de cada instancia lee hasta 1,000 negocios (los primeros por nombre) y se rehace cada 30 segundos con una consulta sobre todos. El siguiente paso es una tabla del directorio mantenida con eventos de caja, menú y horario, que hoy no existen. Con varias instancias, dos peticiones pueden ver estados distintos durante esos 30 segundos.
  - No hay ubicación ni distancia: la búsqueda encuentra la ciudad de la tarjeta. Tampoco hay mapa, favoritos ni carrito entre negocios. Desde la 24B, Explorar dice qué negocio llega al código postal del cliente, y «Pedir otra vez» está en Mis pedidos.
  - Las categorías son fijas y una por negocio. Agregar una necesita migración (la CHECK), enum, cliente y la lista del registro.
  - El perfil de un cliente (`/profile`) sigue en el marco del equipo, con «Mis pedidos» en el menú. Explorar y Mis pedidos ya tienen el suyo.
  - «Ver más» pide cada página por separado sobre una foto que puede rehacerse entre una y otra: un negocio que cambia de grupo puede repetirse u omitirse entre páginas.
  - La 24A suma 330 B a la primera visita (de 176,806 a 177,136 B, contra `master` en `c3c1a00`) y sube el presupuesto a 177,200 B. Una parte son los enums del contrato, que viven en el primer chunk aunque solo los usen pantallas que bajan aparte.
- Comprar con tu cuenta (Fase 24B, [ADR 0022](../adr/0022-shopping-with-an-account.md)):
  - El carrito guardado y el código postal de Explorar viven en el dispositivo (`localStorage`): no siguen a la cuenta a otro teléfono ni a otra computadora, y un navegador que bloquea el almacenamiento los pierde al recargar.
  - Las direcciones de la cuenta (hasta 10) no se validan contra un mapa: el código postal decide la zona, como en la 22B. No hay una dirección predeterminada: la tienda ofrece primero la última usada.
  - El tope de 10 y «una por lugar» se revisan antes de guardar, sin restricción en la base: dos guardados a la vez de la misma cuenta pueden dejar una de más o repetida. Se borra en Cuenta.
  - La tarjeta de Explorar da el envío de la zona, no el total: el pedido mínimo y el envío gratis los aplica el menú al pedir.
  - «Pedir otra vez» solo regresa cantidades enteras: un producto por kilo o en fracciones no regresa, porque la tienda pide por pieza.
  - Un cliente anterior, sin llave del pedido, todavía puede registrar dos veces el mismo pedido.
  - La 24B suma 125 B a la primera visita (de 177,136 a 177,261 B, contra `master` en `d7e54f5`) y sube el presupuesto a 177,400 B.
- Seguimiento (Fase 24C, [ADR 0023](../adr/0023-order-tracking.md)):
  - El cliente se entera de un cambio con hasta 20 segundos de retraso: las pantallas vuelven a preguntar, no hay SignalR para clientes ni avisos en el navegador.
  - El enlace de seguimiento es el secreto: quien lo tenga ve el pedido durante 30 días, y no se puede revocar ni volver a emitir. Se guarda tal cual; guardarlo como hash quitaría además la nota de las mayúsculas.
  - El enlace no llega por WhatsApp: un invitado que cierra el recibo sin abrirlo, copiarlo ni guardarlo lo pierde.
  - `rollback.sql` no se detiene si hay enlaces emitidos: los quita y dejan de abrir (está en su encabezado).
  - La base compara el enlace sin mayúsculas (la intercalación de la columna); el servidor exige el exacto. El índice único también es sin mayúsculas: dos enlaces que solo difieren en mayúsculas chocarían, algo que con 32 bytes aleatorios no se espera.
  - El motivo de una cancelación lo escribe el negocio, y el enlace lo muestra como lo mostró WhatsApp.
  - Los pedidos anteriores a la 24C, los del mostrador y los de WhatsApp no tienen enlace; los de una cuenta se siguen en Mis pedidos.
  - En curso trae hasta 50 pedidos, sin páginas: los que pasen de 50 no salen en ninguna sección hasta que terminen o cumplan 30 días.
  - La 24C suma 311 B a la primera visita (de 177,261 a 177,572 B, contra `master` en `29a81a8`) y sube el presupuesto a 177,800 B. Como en la 24A, una parte son los enums del contrato, que viven en el primer chunk.
- Citas en línea (Fase 24D-1, [ADR 0025](../adr/0025-online-booking.md)):
  - Son en el negocio: no se agenda a domicilio en línea, aunque el negocio haga visitas (Fase 22E).
  - El cliente no elige quién lo atiende: «Citas a la vez» es del negocio, no por persona, y la solicitud llega sin persona asignada.
  - El paso de 30 minutos, la hora de anticipación, los 30 días y las 3 solicitudes por teléfono son fijos, no por negocio.
  - Una solicitud que nadie decide ocupa su hora hasta que pasa; no vence sola ni avisa al cliente que no se decidió.
  - El candado de las solicitudes (`sp_getapplock`) solo existe en SQL Server: en otro proveedor dos clientes distintos podrían tomar la última hora. La llave de la solicitud sí evita la doble petición en cualquiera.
  - El enlace de la cita, como el de un pedido, no se puede revocar ni volver a emitir, y no llega por WhatsApp: el aviso de la solicitud no lo lleva.
  - Sin anticipo ni recordatorio de que confirme: quien no llega deja la hora vacía.
  - Sin tiempo real: la agenda abierta ve una solicitud nueva al volver a cargar.
  - Cualquiera puede pedir una cita con un teléfono ajeno y hacer que el negocio le mande un WhatsApp: solo lo frenan las 3 solicitudes por teléfono y el límite por IP (compartido con los pedidos). Falta captcha o verificación del teléfono.
  - El negocio no recibe aviso cuando el cliente cancela: lo ve en la agenda.
  - En la agenda, una solicitud que nadie decidió sigue «Por confirmar» después de su hora (la cuenta del cliente ya dice «No se confirmó»), y Aceptar responde que ya terminó.
  - Mis citas trae solo las citas agendadas en línea con la cuenta: no las que el negocio registró con el teléfono del cliente.
  - La 24D-1 suma 286 B a la primera visita (de 177,674 a 177,960 B, contra `master` en `d21f0f2`) y sube el presupuesto a 178,200 B.
- El menú en línea con la forma de las apps de entrega (Fase 24E, [ADR 0026](../adr/0026-store-like-delivery-apps.md)):
  - Un negocio sin portada ni fotos se ve con sus colores y las iniciales de sus productos, y «Destacados» no aparece. Es correcto, pero menos vistoso que con fotos: depende de que el dueño las suba.
  - Explorar sigue con las tarjetas de la 24A: no muestra la portada.
  - La búsqueda es en el navegador, sobre lo que el menú ya trae (hasta 500 productos), y compara palabras, sin errores de dedo.
  - La flecha de regreso depende del historial de la app: un menú abierto en una pestaña nueva desde Explorar no la ve.
  - Una línea del pedido no se edita: se cambia su cantidad, o se quita y se agrega con otras opciones o instrucciones.
  - La portada se guarda en las mismas variantes que el logo (hasta 2048 px de ancho): no hay recorte ni enfoque elegido por el dueño, el menú la recorta al centro.
  - La 24E suma 49 B a la primera visita (de 177,960 a 178,009 B, dentro del presupuesto de 178,200 B).
- Buscadores y vistas previas (Fase 24D-2, [ADR 0027](../adr/0027-search-engines-and-link-previews.md)):
  - Las imágenes de la vista previa son WebP, como todas las variantes de la app; algunas apps antiguas no las muestran.
  - Los datos estructurados de una tienda no incluyen los días especiales ni los productos (precios y fotos).
  - El sitemap sale de la foto de Explorar: hasta 1000 negocios y sin la fecha en que cambió cada tienda.
  - Los títulos de la tienda y de sus citas se escriben en dos lugares, el servidor (`PublicPages`) y el cliente (`storePageTitle` y la página de citas); las pruebas de los dos los fijan.
  - Sin `AppUrl` configurada, la dirección canónica es la de la petición.

- Resuelto en la Fase 14: `AuthController` (440 líneas, 33 acciones) se dividió en cinco controllers con exactamente las mismas rutas, acceso y límite de peticiones: sesión (`AuthController`), registro y confirmaciones (`RegistrationController`), recuperación de contraseña (`PasswordRecoveryController`), perfil (`ProfileController`) y el negocio de la sesión (`MyTenantController`). `AccountEndpointsTests` fija las 33 rutas con quién puede llamarlas y su límite; el contrato OpenAPI es el mismo (solo cambia el orden en que lista 24 rutas de la cuenta). El webhook de WhatsApp ya no usa el DbContext: verifica con `VerifyWhatsAppWebhookTokenQuery`, deja la entrega en la bandeja y los estados de entrega se aplican con `ApplyWhatsAppStatusesCommand` (Fase 7).
- Componentes frontend de 700–1,500 líneas (Vender, Pedidos, Nuevo pedido) y `types/index.ts` monolítico. Inventario se dividió por pestaña y diálogo en la Fase 7 y sirve de patrón.
- Las pruebas del cliente (Vitest y Testing Library, desde la Fase 5) cubren utilidades de dinero, unidades, acceso, errores, IVA, mensajes de WhatsApp y componentes con existencias; faltan pruebas de pantallas completas y de extremo a extremo. (Desde la Fase 4F hay pruebas contra SQL Server 2022 en CI para folios y proyecciones.)
- El slot de staging con swap y aprobación está listo en el workflow, pero sin activar (Fase 7, [despliegue](../development/deploy-azure.md)): requiere plan Standard o superior, OIDC, crear el slot, la credencial federada del environment `production` y sus revisores. Mientras tanto cada push a `master` despliega directo a producción; la prueba de humo confirma la versión nueva y que la base tenga sus migraciones (`/health/schema`).
- `PendingModelChangesWarning` se ignora en ejecución; el CI lo valida con `has-pending-model-changes`.
- Cambios de estado, cancelaciones, reembolsos y el cierre de una cuenta abierta antes de la Fase 14 (que descuenta sus rondas de entonces) toman su folio `INV` fuera de una transacción: si fallan después, dejan un hueco. Desde la Fase 14 cada ronda de una cuenta corre en una transacción, como la venta. Hacerlos transaccionales exige quitar sus reintentos internos ante `DbUpdateConcurrencyException` (con MARS, SQL Server no usa savepoints y el reintento repetiría lo ya ejecutado) o desactivar MARS tras revisar que nada lo necesite. Mientras tanto, regresar a una versión anterior a la Fase 4 (que numeraba `INV` contando los movimientos del día) puede chocar con esos huecos hasta que cambie el día UTC.
- Las migraciones de producción se aplican a mano con el `apply.sql` de cada carpeta antes del merge (probadas en CI con `sqlcmd`); `/health/schema` detecta las que falten, pero ningún paso del despliegue las aplica con aprobación.
- Categorías, almacenes y agentes de IA se listan completos porque cada negocio tiene un tope (300, 100 y 20, contando los archivados o inactivos; Fase 12). Archivar no libera lugar: al llegar al tope no se crean más. La cocina muestra los 200 pedidos y las 200 comandas más recientes, para que los viejos que nadie cerró no tapen los nuevos; con más pendientes, los más viejos no aparecen.
- Resuelto en la Fase 14: Inicio ya no carga en memoria las órdenes y pagos del rango. Los ingresos salen del resumen diario de los reportes (`DailySalesSnapshot` para los días cerrados y sumas en la base para el resto) y lo demás de agregados en SQL; las cifras son las mismas que antes (`DashboardMetricsSqlServerTests` las compara con el cálculo anterior). El rango son días completos del negocio, así que el último segundo del día, que el filtro de la pantalla dejaba fuera, ahora cuenta; un periodo abarca hasta 366 días.
- Desde la Fase 12 una consulta que incluye colecciones las lee en sentencias separadas (`SplitQuery` por defecto). Fuera de una transacción, esas sentencias pueden ver un cambio hecho entre una y otra; las escrituras se protegen con los tokens de concurrencia, no con lo leído.
- `client/README.md` sigue siendo template genérico.
- Todo cobro que recibe el negocio, y todo reembolso de ese dinero, exige una caja abierta que lo tome, así que ya no se guardan pagos sin movimiento de caja (los guardados antes siguen fuera de los turnos). Desde la Fase 22F la excepción es el pago «Plataforma»: lo cobró Rappi, Uber Eats o DiDi Food en su app, así que no entra a ninguna caja, y su reembolso tampoco ([ADR 0013](../adr/0013-delivery-platform-orders.md)). El corte del día lo muestra aparte.
- “AI Agent” conserva campos de modelo/temperatura/memoria aunque la implementación es por palabras clave, lo que puede inducir a error.
- Un negocio sin plan (todos los anteriores a la Fase 3) puede activar cualquier módulo desde `PUT /auth/my-tenant`; con plan, solo los incluidos.
- Las actions fijadas por SHA y las imágenes de Docker fijadas por digest no se actualizan solas: no hay Dependabot, y los procedimientos están en [despliegue](../development/deploy-azure.md#columnas-borradas-imágenes-por-digest-y-cobertura-de-la-suite-fase-13). Una imagen fijada no recibe los parches de seguridad de su etiqueta hasta que alguien cambia el digest.
- Cocina por producto: un negocio que no es de comida y activa el módulo de cocina no ve comandas, porque la cocina toma los pedidos por giro y no por producto.
- Resuelto en la Fase 16: los dueños eligen el tema de su negocio entre ocho paletas curadas (Configuración › Negocio › Apariencia, [ADR 0010](../adr/0010-business-themes.md)). Su equipo ve la app con esos colores y sus clientes, el menú en línea; cada par de colores pasa WCAG AA en claro y en oscuro, y una prueba lo exige.
  - Paleta: los colores de estado de Hyper (verde 2.0:1, rojo 3.0:1, amarillo 1.6:1, azul 2.5:1 con texto blanco) se cambiaron por unos AA en los dos modos; el texto general sube de 4.28:1 a 7:1 sobre la página.
  - Modo oscuro: tiene fondo oscuro (antes la página se quedaba clara detrás de tarjetas oscuras), con alertas, chips, chat de WhatsApp y fichas en versión oscura.
  - Consistencia: Cobrar usa el color principal en Vender, Mesas y Pedidos, y ningún estado se pinta con el acento.
  - Pendiente:
    - Hyper salió en la Fase 20E (antes, en la 20B, los 60 KB de `vendor.min.css` sin uso). `theme.css` todavía cubre uno por uno colores que Bootstrap compila con los valores de Hyper.
    - No hay color libre de marca, solo los temas curados.
    - El logo del negocio todavía no salía en el ticket ni en el selector de negocio; se resolvió en la Fase 19.
- En curso, Fase 20: retirar Hyper, empezando por la entrega del cliente (un PR por subfase).
  - Resuelto en la 20A: la API manda los scripts y estilos con las copias Brotli y gzip que deja el publish, los guarda en caché un año (`immutable`, porque su nombre lleva el hash) y revisa el `index.html` en cada visita (`no-cache`). La primera visita al menú en línea bajó de 1,274,580 B a 272,273 B. La prueba de humo del despliegue lo exige ([entrega del cliente](../development/deploy-azure.md#entrega-del-cliente-comprimida-y-en-caché)).
  - Resuelto en la 20A: una pantalla del negocio abierta antes de un despliegue avisa "Hay una versión nueva de TienditApp" (al volver a la pestaña, al recuperar la conexión y cada 10 minutos); antes seguía con la versión con que se abrió hasta que alguien recargaba. Sin conexión no ofrece recargar, y queda debajo de los diálogos (nunca encima de Cobrar). El menú en línea y el inicio de sesión no lo muestran.
  - Resuelto en la 20A: el menú en línea publicaba la dirección fiscal del negocio desde el despliegue de la Fase 19; ahora publica la de la sucursal principal, y Configuración dice cuál se ve ([ADR 0011](../adr/0011-opening-hours.md), punto 7).
  - Resuelto en la 20A: `/uploads//orders/...` (y `//uploads/...` o con `\` en Windows) se saltaban el bloqueo de los comprobantes que aún no se mueven al almacenamiento privado; el bloqueo ahora lee la ruta como el sistema de archivos.
  - Resuelto el 2026-10-10, después de la 20A: Vender guarda la llave de idempotencia del cobro con el borrador de la venta antes de enviarlo. Antes vivía en memoria: si alguien recargaba mientras se enviaba un cobro (F5 o el navegador), el carrito volvía con una llave nueva y el siguiente intento podía registrar una segunda venta. Ahora vuelve con su llave y el servidor responde con la venta que ya tenía (`SellPage.test.tsx`, `OfflineSalesTests`). La llave se suelta cuando la venta queda registrada o cuando cambia el carrito (un producto, una cantidad, una nota o el cliente), porque eso ya es otra venta y lleva llave nueva. Pendiente: si el primer intento sí llegó y el cajero cambia el carrito antes de reintentar, el nuevo intento registra una segunda venta, porque Vender no sabe si el primero llegó.
  - Resuelto en la 20B: las escenas visuales viven en `client/visual` (Playwright sobre el build de producción, API simulada en modo estricto, línea base de `master` y cero píxeles de tolerancia, con un muestrario que compara estilos calculados y variables en claro, oscuro y los 8 temas). `scripts/check-bundle.mjs` vigila en CI y en el despliegue lo que baja la primera visita. Salieron `vendor.min.css` y las reglas de `theme.css` que nada dibujaba: el CSS global bajó de 93,691 B a 85,889 B con gzip.
  - Resuelto en el cierre de la 20B: 231 escenas más (Vender y Pedidos, Clientes y Mensajes, Caja y Configuración, las pantallas públicas y de cuenta con el marco): 293 capturas en total y cada ruta de `App.tsx` con escena. Comparadas con el `master` de antes de la 20B, salen iguales: quitar `vendor.min.css` y las reglas muertas no movió ninguna pantalla. Las escenas salen iguales en cada corrida: sin transiciones ni animaciones CSS y con repintado de mosaicos completos. Antes, dos de ellas cambiaban según la carga de la máquina, aun entre dos corridas del mismo `master`.
  - Pendiente en los datos simulados compartidos (`client/visual/mocks`), que cada grupo cubre en su archivo de escenas:
    - registrar un pago devuelve el pago y no el pedido que espera el contrato;
    - la caja abierta no trae `isMine`, `cashCount` ni el nombre de quien la abrió, y el corte del día no trae las propinas ni los nombres;
    - los turnos de caja incluyen uno de otro negocio;
    - el menú público no trae `ordersPaused`, la dirección ni el horario;
    - no hay respuesta de acceso para el equipo de la plataforma;
    - el pedido simulado no cobra las variantes ni los extras.
  - Resuelto en la 20C: el marco de la app es nuestro (`shell.css`, clases `ta-*` y variables `--ta-*`, sin SimpleBar), con cero diferencias contra `master` en las escenas. El menú cerrado del teléfono ya no deja enfocar sus enlaces invisibles. La primera visita baja 5.8 KB con gzip (JavaScript −7.1 KB, CSS +1.3 KB mientras `app.min.css` siga cargado).
  - Resuelto en la 20D: las clases que la app tomaba solo de Hyper (tamaños de letra, avatares, botones suaves, títulos de tarjeta, ojo de la contraseña, pestañas con línea, paginación redonda y tablas centradas) tienen regla propia en `components.css`, con cero diferencias contra `master` en las 313 escenas. `hyperClasses.test.ts` falla si el código usa una clase que solo existe en Hyper. El CSS global sube 0.3 KB con gzip mientras `app.min.css` siga cargado.
  - Resuelto en la 20E: Bootstrap 5.3.8 se compila desde Sass con los valores de Hyper (`assets/scss`), en lugar de la hoja que compiló Hyper, que salió con sus imágenes. Cero diferencias contra `master` en las escenas. El CSS global baja de 607 KB a 403 KB (de 87.5 KB a 62.1 KB con gzip).
  - Resuelto en la 20F: los arreglos visibles que la 20C, la 20D y la 20E dejaron anotados para no mover nada, cada uno con su antes y después:
    - **Foco del teclado:** enlaces y botones no mostraban dónde estaba el teclado (`outline: 0 !important` de Hyper), y los resplandores de Bootstrap no llegaban a 3:1 o no salían (`.btn-light`, `.btn-dark`, sus contornos y los botones suaves menos el principal). Ahora todo dibuja el mismo anillo de `theme.css`, 2 px del acento a 2 px del elemento, que pasa 3:1 en cada superficie de cada tema.
    - **Campos de 16 px en el teléfono:** con menos, Safari de iOS acercaba la página al enfocar un campo y la dejaba así.
    - **Contraste del modo oscuro:** el texto general, el secundario y el del menú lateral pasan AA en cada superficie oscura, y lo que iba en blanco sobre el acento (que en modo oscuro es claro) usa el texto del tema para él, también la marca de las casillas, radios e interruptores marcados.
    - **Menú condensado de las tablets:** ya no alarga cada página a 2000 px, su nombre al pasar el puntero se lee en modo oscuro, el menú lateral avisa con un borde desvanecido que tiene más enlaces abajo, y el nombre y el rol de la cuenta ya no son encabezados dentro de un botón.
    - **Pendientes chicos:** el botón suave gris se lee sobre su tinte y deshabilitado lo conserva, `font-12`…`font-22` crecen con la letra del navegador y el botón de borrar de las búsquedas queda como lo dibuja Bootstrap 5.3.8.
    - **Lo que encontró la revisión del diff:** Safari antes de 15.4 descartaba la regla del anillo entera (ahora los campos tienen la suya y, sin `:focus-visible`, casillas y páginas lo dibujan con cualquier foco); en oscuro el filtro del botón de cerrar volvía gris el anillo; el borde desvanecido del menú podía tapar el enlace enfocado; y el menú condensado ya no depende de una caja que ignora el puntero en las pantallas táctiles.
    - Cambiaron 270 de 321 escenas, y un arreglo explica cada una: se aplicaron uno por uno, con una corrida completa en cada paso. Las 8 nuevas (foco, menú condensado y búsqueda) pasan dos corridas seguidas. El CSS global sube 0.7 KB con gzip (a 62.8 KB) y el JavaScript inicial 0.4 KB.
  - Pendiente después de la 20F:
    - Con los campos de 16 px, la barra de filtros de una lista ocupa más en el teléfono (en Pedidos, de 3 a 5 renglones): un botón "Filtros" que la abra.
    - En modo claro quedan dos colores de Hyper debajo de AA sobre blanco: el texto de error de un campo (`#fa5c7c`, 3.1:1) y el del código (`#39afd1`, 2.6:1).
    - La paginación deshabilitada no se ve atenuada.
    - En el menú condensado de las tablets, los enlaces no tienen nombre accesible (su texto va oculto con `visibility: hidden`) y el teclado no muestra el nombre que muestra el puntero. Las escenas del menú condensado buscan los enlaces por su ruta mientras tanto; ya pasaba en `master`.
    - El menú condensado se desplaza con el dedo en una pantalla táctil sin ratón; con un iPad con trackpad o ratón toma el comportamiento del escritorio, que no se ha probado con el dedo en iOS.
    - Los tipos de los datos simulados de las escenas no se comprueban contra el contrato: baseline y comparación usan los mismos, así que no esconden una regresión visual, pero pueden alejarse de la API real.
- Resuelto en la Fase 22E: la dirección de una cita a domicilio iba en las notas, el cargo por ir se cobraba de memoria, nada dejaba tiempo de traslado entre una casa y otra y la venta no sabía qué cita cobraba ([ADR 0020](../adr/0020-home-visits.md)).
  - Citas a domicilio con la dirección del cliente y el cargo por visita de su zona de entrega, tiempo de traslado por negocio, ruta del día por persona con Maps, y «Cobrar» en la Agenda, que abre Vender con la cita y cobra el cargo como concepto propio con su IVA, una vez por cita.
  - Queda:
    - La ruta va en orden de hora, sin optimizar, y el traslado es un tiempo fijo: no hay mapa propio, geocodificación ni distancia real.
    - No se agenda a domicilio desde el menú en línea (las citas en línea de la 24D-1 son en el negocio), y las direcciones guardadas del cliente no tienen código postal: la dirección se escribe en la cita.
    - Dos citas guardadas al mismo tiempo para la misma persona pueden traslaparse (la revisión del traslape y del traslado es en Application, sin restricción en la base), como antes.
    - Editar una cita sin `visit` la deja en el negocio: un cliente abierto antes del despliegue que edita una visita la quitaría hasta recargarse (`deploy-azure.md`).
    - Dos ediciones simultáneas de una cita ahora dan 409 en lugar de quedarse con la última.
    - Una plantilla de recordatorio propia del negocio no sabe si la cita es a domicilio: dice lo que el negocio escribió (sin plantilla, el recordatorio dice «a domicilio (CP …)»).
    - La agenda dice «Con venta» aunque la venta no esté pagada: el saldo se ve en Pedidos.
    - La clave del SAT del cargo por visita es la del primer servicio de la venta, o 01010101: cada negocio debe confirmarla con su contador.
    - El personal con `ManageAppointments` que no es dueño ni admin no lee la lista del equipo (`GET /employees`), así que su ruta del día no filtra por persona: ve la de todos.
    - Volver a la versión anterior no muestra las visitas ni el cargo, y una cita cobrada se podría cobrar otra vez (`deploy-azure.md`); el `rollback.sql` se detiene (50015).
    - La 22E suma 79 B a la primera visita (de 176,728 a 176,807 B, contra `master` en `5aac2b7`): solo el mapa de módulos que cargan las pantallas que bajan aparte (la Agenda, Vender y Zonas de entrega comparten dos módulos nuevos), cuyo tamaño comprimido cambia unos bytes con los nombres de los archivos. Pasaba el presupuesto (176,800 B) por unos bytes, así que sube a 176,900 B.
- Resuelto en la Fase 22D: nadie sabía cuándo salía un pedido ni qué cobraba el repartidor, y el efectivo que entregaba Paquetenvia se anotaba en una libreta ([ADR 0019](../adr/0019-courier-cash-on-delivery.md)).
  - «Salió a entregar» con lo que cobra en efectivo y con cuánto paga el cliente, aviso «En camino» por WhatsApp, lo que debe el repartidor y su liquidación en Caja › Repartidores, que cobra los pedidos en la caja de quien recibe y deja el viaje y la comisión como gastos.
  - Queda:
    - Paquetenvia sigue sin integración: el equipo marca la salida y la entrega, y TienditApp no sabe cuánto cobró de verdad.
    - Un solo repartidor en el catálogo (Paquetenvia).
    - Sin saldo a favor del repartidor: lo que entrega de más se registra como «Otro» en esa liquidación.
    - Una liquidación no se anula ni se edita: se corrige con un reembolso o un movimiento de caja.
    - Volver a la versión anterior no muestra la salida ni lo que debe el repartidor (`deploy-azure.md`); el `rollback.sql` se detiene (50014).
    - La 22D suma 126 B a la primera visita (de 176,614 a 176,740 B, contra `master` en `516f92b`) y sube el presupuesto a 176,800 B.
- Resuelto en la Fase 22C: el pedido no sabía a dónde iba ni cobraba el envío, y el folio de Paquetenvia se anotaba en papel ([ADR 0018](../adr/0018-home-delivery-orders.md)).
  - El menú en línea y Vender toman pedidos a domicilio con la zona que llega al código postal, su mínimo y su envío, cobrado como concepto propio con su IVA y en el borrador de CFDI. Pedidos muestra la dirección y guarda el folio y el enlace del viaje de Paquetenvia; la cocina y el ticket lo dicen.
  - Queda:
    - Paquetenvia se anota a mano: no hay integración con su API ni seguimiento del repartidor dentro de TienditApp.
    - La dirección no se valida contra un mapa y el cliente del menú no guarda sus direcciones.
    - No hay tiempo estimado de entrega ni horario por zona.
    - La clave del SAT del envío (78102203) es la más común; cada negocio debe confirmarla con su contador.
    - Anotar el viaje de Paquetenvia no avisaba en tiempo real: desde la Fase 22D, `OrderUpdated` refresca Pedidos.
    - Un negocio con precios sin IVA que escribió sus envíos con IVA incluido cobraría el IVA dos veces: la consulta 6 del `verify.sql` los lista antes del merge, y las pantallas y el bot dicen «más IVA».
    - Volver a la versión anterior cobra los pedidos a domicilio por su total, pero no muestra su dirección ni su envío (`deploy-azure.md`); el `rollback.sql` se detiene (50013).
    - La 22C suma 125 B a la primera visita (de 176,481 a 176,606 B, contra `master` en `0eaa1d1`). El presupuesto no cambia.
- Resuelto en la Fase 22B: el negocio no sabía a dónde entrega ni cuánto cobra por llevar ([ADR 0017](../adr/0017-delivery-zones.md)).
  - Zonas de entrega por código postal, con costo de envío, pedido mínimo y envío gratis desde un monto, en Configuración › Zonas de entrega. Las respuestas automáticas de WhatsApp dicen si llegan a un código postal y cuánto cuesta.
  - Queda:
    - El menú en línea con «A domicilio», el verificador de código postal en el menú, la dirección del cliente y el envío cobrado en el pedido (con su concepto en el CFDI) llegaron en la Fase 22C.
    - No hay horario por zona ni tiempo estimado de entrega.
    - Una zona llega a todo un código postal o a nada, aunque algunos códigos grandes cubran colonias a las que el negocio no llega.
    - Una pregunta de envío con un número de cinco dígitos que no es un código postal recibe la respuesta de un código postal.
    - Las zonas son del negocio: todo pedido sigue llegando a la sucursal principal.
    - La 22B suma 154 B a la primera visita (de 176,327 a 176,481 B, contra `master` en `3e69f32`): la entrada del menú y la ruta. El presupuesto no cambia.
- Resuelto en la Fase 22A: una cocina sin local publicaba su dirección, muchas veces su casa, en el menú en línea ([ADR 0016](../adr/0016-delivery-only-branches.md)).
  - Cada sucursal dice cómo atiende. Con la principal «Solo a domicilio», el menú dice que no hay local para visitar y la respuesta automática a «¿dónde están?» también; ninguna lleva la dirección, que sigue viendo el equipo. El giro «Comida a domicilio» empieza así, y la guía de inicio ofrece a todo negocio de comida la plantilla «Comida a domicilio».
  - Queda:
    - Volver a la versión anterior de la app publica otra vez la dirección de una principal solo a domicilio: el aviso está en `deploy-azure.md` y el `rollback.sql` se detiene (50011).
    - Las mesas y el horario siguen siendo del negocio, no de cada sucursal: un negocio con un local y una cocina aparte no puede tener horarios distintos.
    - El giro decide la sucursal principal solo en el alta; en un negocio que ya existe se marca a mano en Sucursales.
    - La entrega llegó en las Fases 22B (zonas por código postal) y 22C (dirección del cliente en el pedido, envío cobrado y Paquetenvia).
    - La 22A suma 1 B a la primera visita (de 176,326 a 176,327 B, contra `master` en `a60b1d4`): el enum de la sucursal. El presupuesto no cambia.
- Resuelto en la Fase 22G: la liquidación de cada depósito de Rappi, Uber Eats y DiDi Food y su conciliación con el estado de cuenta ([ADR 0015](../adr/0015-delivery-platform-settlements.md)).
  - Reportes › Liquidaciones dice lo que debe cada plataforma por sus pedidos pendientes. El dueño o un administrador captura cada depósito, TienditApp propone los pedidos del periodo, lo que no cuadra se explica con ajustes con motivo (el 35 % que cubre Rappi, los faltantes, el saldo de DiDi, la publicidad, las cancelaciones tardías…) y la liquidación solo se cierra con la diferencia en $0.00.
  - Cerrar vuelve a calcular cada pedido y, si una devolución cambió alguno, pide revisarlo; anular conserva la liquidación y libera sus pedidos; las dos quedan en la bitácora.
  - Pendiente:
    - El estado de cuenta se captura a mano: importar el archivo de cada plataforma, o leerlo de su API, llega con las integraciones.
    - Un depósito no es un movimiento de caja ni de banco: TienditApp no lleva cuentas de banco ni concilia contra el estado de cuenta del banco.
    - Con liquidaciones guardadas, el rollback de la base se detiene (error 50010): hay que exportarlas y borrarlas antes, o corregir hacia adelante. Volver a la versión anterior de la app sí funciona sin tocar la base.
    - Lo escrito en una liquidación se pierde sin preguntar al cambiar de pantalla dentro de la app (`BrowserRouter` no permite detener una navegación); cerrar o recargar la pestaña sí pregunta, como en Precios en apps.
    - Una liquidación ofrece hasta 2,000 pedidos pendientes: con más hasta el final del periodo, van primero los del periodo y, en el lugar que quede, los más recientes de antes; los más viejos esperan en Por cobrar a otra liquidación. Un periodo con más de 2,000 pedidos propios tiene que partirse.
    - Un pedido que el negocio cobra en dos partes y se liquida con la primera no trae la segunda a Por cobrar, como una devolución posterior: se explica con un ajuste.
    - «Por cobrar» suma el neto esperado de cada pedido redondeado por separado, y Ventas por canal redondea por fila: pueden diferir por centavos.
    - La 22G suma 317 B a la primera visita (de 176,009 a 176,326 B, contra `master` en `fa7c761`): la sección del menú, las dos rutas, los dos enums del contrato que usan las pantallas y lo que `DataTable` ganó en la revisión (no pasarse de la última página con menos filas, y el largo de la búsqueda); las pantallas, su API y sus etiquetas se cargan aparte. El presupuesto sube de 176,400 a 176,700 B y queda a 374 B: el siguiente crecimiento tendrá que subirlo y decir por qué.
    - En el teléfono, las cuentas y «Guardar» quedan después de los pedidos y los ajustes, en el orden de un formulario; la tabla de pedidos se desplaza por dentro (hasta el 65 % de la pantalla). Una barra fija abajo tendría que convivir con la barra de pestañas.
- Resuelto en la Fase 22F-2: el precio de cada producto y opción en la app de cada plataforma ([ADR 0014](../adr/0014-delivery-platform-prices.md)).
  - Catálogo › Precios en apps pone lo que cuesta cada producto, y lo que suma cada opción, en Rappi, Uber Eats y DiDi Food, con un cálculo por porcentaje que la persona revisa antes de guardar. Sin precio propio, se cobra el del menú.
  - Vender carga el catálogo con los precios de la plataforma elegida y no deja registrar hasta tenerlos; la venta cobra los mismos, así que el total, la comisión, las retenciones y el neto esperado salen de lo que cobró la app.
  - Pendiente:
    - Los precios se capturan a mano: si alguien cambia uno en la app y no aquí, el pedido sale con el anterior. Leerlos de cada plataforma llega con las integraciones.
    - Sin bitácora de cada cambio de precio, igual que los del menú; cada pedido copia los que cobró.
    - Fuera de alcance: las promociones de la plataforma, los precios por sucursal y un menú, una disponibilidad o un horario distintos por plataforma.
    - Con precios guardados, el rollback de la base se detiene (error 50009): hay que exportarlos y borrarlos antes, o corregir hacia adelante. Volver a la versión anterior de la app sí funciona sin tocar la base.
    - Lo escrito en Precios en apps se pierde sin preguntar al cambiar de pantalla dentro de la app: `BrowserRouter` no permite detener una navegación (`useBlocker` pide un router de datos). Cerrar o recargar la pestaña sí pregunta.
    - Con más de 500 productos, Precios en apps muestra los primeros 500 y avisa que los demás se cobran con el precio del menú.
    - Dos cambios del mismo precio ya guardado no chocan (sin token de concurrencia): gana el último, como en el menú.
    - La 22F-2 suma 150 B a la primera visita (de 175,859 a 176,009 B, contra `master` en `9c17a90`): la entrada del menú y la ruta de Precios en apps; la pantalla y sus ganchos se cargan aparte. El presupuesto sube a 176,400 B y queda a 391 B: el siguiente crecimiento tendrá que subirlo y decir por qué.
- Resuelto en la Fase 22F: pedidos de Rappi, Uber Eats y DiDi Food con captura manual ([ADR 0013](../adr/0013-delivery-platform-orders.md)).
  - El negocio registra en Vender el pedido que llegó por la app, con su folio y quién cobró. Si cobró la plataforma, el pago «Plataforma» no entra a ninguna caja ni pide una abierta. Si cobra el negocio (el repartidor de DiDi paga en efectivo), se cobra por la caja como siempre.
  - Cada negocio configura la comisión, su IVA y las retenciones de cada plataforma, con revisión y bitácora. Cada pedido copia sus términos y los importes calculados, que no cambian después.
  - El corte del día separa lo que cobraron las plataformas («Otros» es ahora el resto), Inicio lo dice debajo de «Cobrado», y Reportes muestra las ventas por canal y los pedidos de cada plataforma con su comisión, sus retenciones y el neto esperado. Nueve invariantes de cuadre (I1 a I9) son pruebas.
  - Un folio repetido el mismo día se pregunta (409 `possible_duplicate_order`) en lugar de bloquearse con un índice único.
  - Los pedidos de plataforma nunca mandan avisos de WhatsApp y quedan fuera de las campañas y de la ficha del cliente.
  - Pendiente:
    - Resuelta en la Fase 22G (arriba): la liquidación semanal y la conciliación con el estado de cuenta. Una cancelación tardía (un pedido ya entregado que la plataforma cancela) sigue quedando entregada y reembolsada: si el pedido todavía no está liquidado, deja de estar pendiente (trae 0); si ya está en una liquidación cerrada, se explica con un ajuste «Cancelaciones» en la siguiente.
    - Integraciones por la API de las plataformas o por un integrador, el CFDI de retenciones y el ID largo del pedido para conciliar.
    - Confirmar con un contador las tasas de retención de 2026 para cada caso (persona física o moral, RFC, pago definitivo; las por omisión son las de la Ley de Ingresos 2026 y la Ley del IVA), la clave de forma de pago 31 y la base de la comisión de Uber Eats.
    - El aviso de folio repetido no es una restricción: dos capturas del mismo folio en el mismo instante pasan las dos.
    - En SQL Server, la parte de la comisión de cada pago se calcula con escala 6 antes de sumarse: con pagos parciales, un punto medio raro puede diferir un centavo del cálculo con toda la precisión.
    - Desde el primer pedido de plataforma no hay rollback de la base (error 50008), solo correcciones hacia adelante. No conviene publicar entre las 00:00 y las 00:45, hora del negocio (ver [migraciones](../development/database-migrations.md)).
    - El badge «En línea» de Pedidos usa el acento (`bg-primary-subtle`), algo que viene de antes; se dejó igual para no mover las líneas base.
- Dependencia con avisos de seguridad (visto en la Fase 22F): `SixLabors.ImageSharp` 3.1.12 (`Directory.Packages.props`) tiene GHSA-j9gm-c75j-xc9q y GHSA-jjfr-hcj7-qf5w (altas) y GHSA-wmxv-xphr-5c9g (moderada), y `dotnet restore` las marca (NU1903, NU1902). Las tres están en el manejo de TIFF y se corrigen en la 4.1.2, una versión mayor. Hoy el riesgo es bajo: `ImageProcessingService` solo registra los decodificadores de JPEG, PNG, WebP y GIF, así que un TIFF subido se rechaza antes de decodificarse. Subirla, revisando su API y su licencia, va en un PR propio.
- Resuelto en la Fase 21: días especiales y horario real en WhatsApp ([ADR 0012](../adr/0012-special-days.md)).
  - El dueño marca por fecha los días que cierra o abre con otro horario (festivos, vacaciones, inventario), con un motivo que ven sus clientes y festivos sugeridos que nunca se aplican solos. Las fechas pasadas no cambian y cada cambio queda en la bitácora.
  - El menú en línea y su pedido los toman en cuenta, también sin horario de la semana, y el servidor escribe lo que dicen ("Cerrado · por Navidad · abre el sábado 26 a las 9:00"); en un día especial cerrado tampoco pide una mesa (decisión por omisión).
  - Las respuestas automáticas de WhatsApp dicen el horario de verdad, con los días especiales de las próximas dos semanas, leído con el mismo lector que el menú.
  - El cambio de horario de Tijuana y Matamoros se evalúa en instantes, y `/health/ready` avisa (`business-time-zones`) si el servidor no tiene las zonas horarias; la prueba de humo lo exige.
  - Pendiente:
    - Horario y días especiales por sucursal.
    - Pedidos programados para cuando abra.
    - Preguntas por una fecha en WhatsApp ("¿abren el 25?") más allá de los días especiales de las próximas dos semanas.
    - Avisar o rechazar citas en los días que el negocio cierra.
    - Ventanas de envío de campañas y recordatorios según el horario.
    - Días especiales que se repiten cada año.
    - Retención de las filas viejas de `SpecialDays` (hoy se conservan todas; la pantalla muestra solo los últimos 90 días).
    - Sugerir los días de elecciones y los festivos estatales.
    - Quitar la redacción anterior del cliente (`describeOpeningStatus`, para una API sin `todayDate`) después de una entrega.
    - Confirmar con el dueño del producto las dos decisiones por omisión: la mesa en un día especial cerrado y la descripción del negocio, que ya no contesta las preguntas de horario cuando hay horario de la semana.
- Resuelto en la Fase 19: el menú en línea vende ([ADR 0011](../adr/0011-opening-hours.md)).
  - El negocio captura su horario (hasta dos por día) y el menú dice si está abierto, en la hora del negocio. Fuera de horario no recibe pedidos para recoger o a domicilio, y el servidor también los rechaza; los de una mesa sí llegan.
  - El encabezado del menú tiene logo o inicial, el horario de la semana, la dirección de la sucursal principal (nunca la fiscal) con "Cómo llegar" y WhatsApp.
  - Los productos sin foto muestran su inicial cuando otros tienen foto, en el menú y en Vender.
  - El logo del negocio sale en el selector de negocio y en el ticket.
  - Las filas de categorías se desvanecen en el borde que esconde más.
  - Pendiente:
    - Resuelto en la Fase 21: no había días festivos ni cierres especiales.
    - El horario es del negocio, no de cada sucursal.
    - No hay pedidos programados para cuando abra.
    - Resuelto en la Fase 21: las respuestas automáticas de WhatsApp no decían el horario.
- Resuelto en la Fase 18: listas e Inventario.
  - Cada lista tiene una sola barra con búsqueda, filtros y su acción, y la paginación va abajo.
  - Pedidos filtra por canal (mostrador, WhatsApp, menú en línea) y muestra "Tipo" solo cuando el negocio vende y da servicios.
  - Inventario tiene una fila de pestañas; Compras y Proveedores son su propia sección.
  - Las existencias usan palabras de todos los días, y las acciones de fila dicen lo que hacen.
  - Pendiente: el filtro Canal sale aunque el negocio no use WhatsApp ni el menú en línea. El cliente no sabe si el negocio recibe pedidos por WhatsApp; decidirlo por el permiso de WhatsApp se lo ocultaría a los cajeros, así que hace falta que la API lo diga.
- Resuelto en la Fase 17: las pantallas de trabajo ya no tienen estorbos.
  - El aviso del plan vencido sale completo solo a dueños y administradores, en Inicio y Configuración; en las demás pantallas va en la campana.
  - "Nueva venta" queda en la barra superior y en la pestaña Vender.
  - El selector de negocio se lee completo en un teléfono de 390 px.
  - Cocina muestra solo la siguiente acción.
  - Las migas solo salen en los detalles.
  - Toda la app usa mayúscula inicial, "negocio" y "correo".
  - Inicio ya no tiene "Pedidos por estado".
  - Pendiente: `DashboardMetricsDto.OrdersByStatus` ya no se muestra en ninguna pantalla. En 360 px, un nombre largo de negocio todavía se recorta. (Las rondas que decían "Preparando" se resolvieron en la Fase 18.)
- Resuelto en la Fase 15: un platillo de una ronda ya enviada se cancela desde la cuenta (`POST /orders/{id}/item-cancellations`): la cuenta deja de cobrarlo, el renglón conserva lo pedido y la cancelación queda con motivo, quién y cuándo; sus insumos regresan al inventario salvo que la cocina ya lo preparara, y la bitácora y la cocina se enteran. Solo se cancela lo que falta por cobrar: un platillo pagado se reembolsa. Pendiente: lo que la cocina ya preparó queda dentro de la salida por venta de su ronda, sin un movimiento de merma propio; volver a la Fase 14 con platillos cancelados en cuentas abiertas los cobra otra vez al recalcular la cuenta ([despliegue](../development/deploy-azure.md#platillos-cancelados-requireopencashsession-y-vender-fase-15)).
- Resuelto en la Fase 14: cada ronda de una cuenta de mesa descuenta sus insumos cuando va a cocina, con la política del negocio como una venta (se rechaza nombrando lo que falta o pasa con aviso); cerrar la cuenta solo descuenta las rondas sin movimiento propio, y cancelarla devuelve lo de todas. Pendiente: volver a la Fase 13 con cuentas abiertas haría que, al cerrarlas, se descuenten otra vez las rondas mandadas con la Fase 14, y que cancelarlas no las devuelva ([despliegue](../development/deploy-azure.md#inicio-authcontroller-y-rondas-fase-14)).
- `Appointment.ServiceProductId` y `StaffUserId` no tienen llave foránea: se validan contra el negocio al guardar.
- Los medios de WhatsApp se guardan como marcador: falta descargar y mostrar la foto o el audio (requiere guardar el archivo por negocio).
- Resuelto en la Fase 25 ([ADR 0024](../adr/0024-service-photos.md)): Pedidos y el tablero de servicios agregan fotos del antes y del después a una orden existente (`ServiceOrderPhoto`, una fila por foto con nota, quién y cuándo), por el agregado y su `rowversion`, validadas por negocio y permiso, con pruebas del acceso entre negocios. Se quitan solo mientras la orden está abierta, por quien las tomó o un administrador. Pendiente:
  - el archivo de una foto quitada, o subida y nunca agregada, se queda en el almacenamiento privado sin uso: no hay limpieza;
  - las fotos de la recepción siguen en `BeforePhotosJson` y `AfterPhotosJson`, sin autor;
  - van por orden, no por pieza, y no tienen plazo de retención.
- Teléfonos de clientes capturados antes de la Fase 4 quedan como se escribieron; la ficha y el alta los comparan en sus formas comunes, pero no se normalizaron en la base.
- Las conversaciones partidas antes de la Fase 4 (`+52` y `+521` del mismo número) siguen como dos hilos; cada mensaje nuevo va al hilo con la misma forma del número.
- No hay lealtad: es por negocio y queda fuera de alcance hasta tener reglas ([ADR 0009](../adr/0009-loyalty-per-business.md)). Las columnas `CustomerProfile.LoyaltyPoints` y `LoyaltyTier`, globales y sin uso, se borraron en la Fase 13.

## Resuelto el 2026-10-01

- Secretos fuera del repositorio (user-secrets/variables de entorno), llave JWT obligatoria fuera de Development y auto-migración/seed limitados a Development con servidor local.
- CORS por allowlist, cabeceras de seguridad, HSTS, cookie de refresh `Secure` fuera de Development y OTP fuera de los logs de producción.
- Membresía revalidada en "mi tenant", marketing, hubs y `TenantOrdersController`; interceptor que rechaza `TenantId` ajeno o modificado; exclusiones del middleware por prefijo; rol `SUPERADMIN` corregido; override de `SUPER_ADMIN` auditado; refresh token rotado al cambiar de tenant.
- Topes de cantidades, piezas y paginación.
- Caja: signo por tipo de movimiento, efectivo separado de otros métodos, una caja abierta por tenant, token de concurrencia y fechas del servidor.
- Órdenes: cancelación unificada y bloqueada con pagos, cocina con órdenes del POS, consumo de piezas en el mismo guardado, órdenes mixtas, concurrencia entre piezas.
- Inventario y compras: costo promedio solo por entradas, referencias reservadas, recepciones sin líneas repetidas, recetas que cambian de insumo y tokens de concurrencia.
- Reporte operativo sin IVA en la utilidad, costo de ventas de órdenes reconocidas y mes del tenant.
- Uploads validados por firma y re-codificados, sin metadatos, con tope de píxeles; evidencias limitadas a archivos del tenant.
- Frontend: sesión expirada, refresh entre pestañas, cambio de tenant, SignalR, IVA en el carrito, acciones de estado/cancelación, caja, chat, reset de contraseña, página de inicio por permisos y error de lint `prefer-const`.
- CI con build, tests, migraciones, lint, build y auditoría; despliegue condicionado a tests y lint; imagen Docker con SPA y usuario no root; npm sin vulnerabilidades.
- Fuga entre tenants del dashboard (productos más vendidos) y escalamiento a `SUPER_ADMIN`: se eliminó `POST /api/v1/auth/register`.
- Webhook de WhatsApp con firma obligatoria, índices únicos e idempotencia (migración `AddWhatsAppWebhookUniqueIndexes`).
- Login con 2FA en dos pasos mediante un desafío de un solo uso; ya no bloquea la cuenta.
- La subida de imagen de producto conserva sus campos (`Product.SetImage`).
- Registro de negocio/cliente y confirmación de teléfono atómicos con la estrategia de reintentos; las órdenes solo se vinculan a teléfonos verificados por SMS.
- Borrado de archivos confinado al namespace del tenant.
- Logs sin códigos OTP ni PII de mensajería y con el teléfono del cliente enmascarado fuera de Development.

## Resuelto el 2026-10-04 (Fase 0 del roadmap de producto)

- La pantalla de inventario usa los mismos números que el dominio para tipos y motivos de movimiento (desde 1, `Other = 99`). Ya se registran "Entrada" y "Compra"; cada tipo mueve las existencias en su sentido; filtros y etiquetas coinciden con lo guardado, incluidos los movimientos del sistema. `ClientEnumContractTests` falla si una copia de enum en el cliente difiere del dominio, e `InventoryEnumValuesTests` fija los valores persistidos.
- Cocina en vivo: eventos de `OrderHub` del grupo del tenant, recarga al reconectar, sondeo cada 30 segundos y solo pedidos de alimentos (sin órdenes de servicio); notas del pedido y por artículo visibles.
- Menú deslizable en celular (modo `full` de Hyper, fondo y cierre al navegar) y tableta contraída; montos de caja con signo.
- Permisos alineados: Dashboard para empleados con `ViewReports`, Cocina para cualquier rol con `ManageServiceBoard`, cambio de estado y cancelación para el mostrador con `ManageOrders`, chat visible para empleados con `ManageWhatsApp`; matriz única en el cliente (`src/utils/access.ts`) para menú, rutas, página de entrada y botones.
- Ventas a público en general sin teléfono (migración `AllowWalkInSalesWithoutCustomer`) y 400 en español para teléfonos inválidos.
- Estados en español en la app y en las plantillas de WhatsApp (`OrderStatusLabels`, `src/utils/labels.ts`).
- Marca TienditApp en lugar de los restos de Hyper y FoodPark Digital; encabezado sin campana ni enlaces muertos.
- Límite de peticiones solo en credenciales y códigos; el cliente ya no cierra sesión ante un 429.
- Reenvío del código de teléfono por `userId` y página `/confirm-email` para el enlace del correo.
- Un solo helper de errores en el cliente.
- Desactivar el 2FA exige un código TOTP o de recuperación, con el lockout de Identity y el rate limit `auth`; Perfil pide el código.

Pendientes detectados en la Fase 0:

- El emisor que ven las apps autenticadoras sale del `JwtSettings:Issuer` ("FoodParkDigital"); cambiarlo exige separar el nombre visible del emisor del JWT.
- La confirmación de teléfono no permite corregir el número capturado.
- El tema visual era el CSS de Hyper (fase 1: tema propio; desde la Fase 16, `theme.css` por capas con el tema de cada negocio). Resuelto en la Fase 20E: Hyper salió y Bootstrap se compila desde Sass con sus valores.

## Resuelto el 2026-10-04 (Fase 1: plataforma)

- API, pruebas e imagen Docker en .NET 10 (LTS) con EF Core y ASP.NET Core 10.0.12 y Serilog.AspNetCore 10, antes del fin de soporte de .NET 9 (10 de noviembre de 2026). El App Service debe usar el stack .NET 10 porque el despliegue es dependiente del framework.
- Solo `master` despliega: una ejecución manual desde otra rama se omite. `develop` conservaba su propio workflow de despliegue y se retiró en la Fase 4.

## Resuelto el 2026-10-04 (Fase 1: vender rápido)

- Política de IVA por negocio (precios con IVA incluido o sumado), tasa por producto (16, 8, 0 % o exento) y desglose por tasa calculado en el servidor y guardado por línea (migración `AddTaxPolicyAndCounterSaleClosing`). Los negocios existentes conservan el IVA sumado al 16 %, con los mismos totales.
- Vender cobra en el mismo paso: efectivo con cambio, pago mixto, comprobante de transferencia y ticket impreso o por WhatsApp; la venta y sus pagos se guardan juntos y la API rechaza la venta si el total mostrado ya no coincide.
- Las ventas de mostrador pagadas se cierran solas (configurable) y Inicio reporta ingresos por pagos registrados, ticket promedio por pedido pagado y saldo por cobrar.
- Nuevo shell: menú por trabajo, "Nueva venta" visible, búsqueda global (Ctrl K) con búsqueda en servidor de pedidos, clientes y productos, selector de negocio en cualquier tamaño y barra inferior en celular.
- Tema propio sobre Bootstrap (tokens, fuente, botones táctiles), avisos (toasts), diálogo de confirmación y `DataTable` con búsqueda en servidor en pedidos, clientes, productos, servicios, empleados, proveedores y citas.
- Arranque guiado con módulos, IVA, catálogos de ejemplo por giro y lista de primeros pasos.

Pendientes detectados en la Fase 1:

- El efectivo recibido y el cambio no se guardan en la venta (solo salen en el ticket). (Resuelto en la Fase 4F: cada pago guarda el efectivo recibido y la propina.)
- No hay staging: la decisión fue publicar solo desde `master` sin slot de prueba.
- Una venta sin conexión no se puede registrar; Vender solo conserva el borrador. (Resuelto en la Fase 3 para ventas de mostrador cobradas; ver [ventas sin conexión](../domain/order-lifecycles.md#ventas-sin-conexión-fase-3).)

## Resuelto el 2026-10-04 (Fase 2: operación confiable)

- Outbox: los efectos durables de los eventos (auditoría y avisos de WhatsApp) se guardan en la misma transacción y los procesa un worker con reintentos, registro por handler y dead-letter (migración `AddOutboxAndWebhookInbox`); bandeja del webhook (`InboxMessages`) y resiliencia HTTP (timeouts y circuit breaker, sin reintentos inmediatos de POST).
- Observabilidad: OpenTelemetry (trazas, métricas y logs) hacia Azure Monitor u OTLP, métricas propias por negocio y `/health/live` y `/health/ready` con el estado de las colas.
- Caja por cajero y sucursal, historial de turnos con diferencias y notas, conteo por denominación y política `RequireOpenCashSession` (migración `CashSessionsPerCashier`).
- Reembolsos parciales con devolución de unidades e insumos, y anulación de ventas pagadas (migración `AddOrderRefunds`, permiso `ProcessRefunds`).
- Campañas, plantillas, bajas BAJA/ALTA y recordatorios de citas por WhatsApp, enviados en segundo plano con historial por mensaje (migración `AddMessagingCampaignsAndReminders`, permiso `ManageCampaigns`).
- Pedido desde el chat (canal WhatsApp en la orden, migración `AddOrderChannel`) y ficha del cliente.
- Autorización v2: regla declarada en cada caso de uso, deny by default con prueba de cobertura, `GET /api/v1/me/access`; cliente de API generado desde OpenAPI con TanStack Query.

Pendientes detectados en la Fase 2:

- El mismo celular mexicano se guarda como `+52…` (capturado) o `+521…` (WhatsApp): la ficha del cliente y las campañas los reconcilian, pero el cliente temporal y la conversación pueden quedar duplicados. Unificar la normalización exige migrar datos y revisar la búsqueda de conversaciones.
- Un envío a Meta que expira puede haberse entregado: el reintento puede duplicar un aviso o un mensaje de campaña (Meta no ofrece idempotencia en envíos).
- Los índices únicos filtrados nuevos (deduplicación de mensajes, caja abierta por cajero, bajas por número) tienen pruebas InMemory y se crearon sin errores en SQL Server 2022 al probar los scripts de producción, y el CI los crea en cada PR (job `Migrations`); faltan pruebas de concurrencia contra SQL Server.
- `OutgoingMessages` no tiene retención y las campañas no respetan horario de envío.
- Pantallas aún sin TanStack Query ni tipos generados; conviven tipos manuales (`types/index.ts`) y generados.
- La actualización de pantallas por SignalR sigue siendo best-effort.

## Resuelto el 2026-10-04 (Fase 3: crecer)

- Cantidades con 3 decimales en kilo, litro, metro y hora (`OrderItem.Quantity` ya no es entero) y datos por línea para CFDI 4.0: claves SAT de producto y unidad, objeto de impuesto y vista previa del comprobante (migración `AddDecimalQuantitiesAndCfdiData`).
- Variantes y extras con precio validado en el servidor, mesas y cuentas abiertas por rondas a cocina (migración `AddProductOptionsAndOpenTabs`).
- Menú en línea con QR, pedidos por enlace y compartir por WhatsApp (migración `AddOnlineMenu`).
- Sucursal como agregado propio para ventas, cajas e inventario, y ventas diarias por sucursal en un modelo de lectura que calcula un worker (migración `AddBranchesAndDailySalesSnapshots`).
- Consola de plataforma: planes con módulos y límites, suscripción por negocio con prueba, pago vencido y suspensión, y pagos registrados (migración `AddPlansAndSubscriptions`). Los módulos de un negocio con plan se recortan al plan.
- Vender sin conexión: ventas de mostrador cobradas guardadas en el dispositivo y enviadas al volver la conexión con clave de idempotencia (migración `AddOfflineSaleIdempotency`).
- La migración de sucursales fallaba en el script idempotente (SQL que usaba columnas agregadas en el mismo lote) y su `Down` siempre se habría negado si un negocio no tenía ubicaciones: ambas cosas se corrigieron antes de publicarla y los scripts de la Fase 3 se probaron en SQL Server 2022.
- El CI aplica las migraciones en SQL Server 2022 como producción (job `Migrations`): el script idempotente con el bloque `SET` y `sqlcmd -b` sobre una base en una migración que producción ya tiene, una segunda ejecución sin cambios, el `Down` hasta esa migración y una base nueva con `dotnet ef database update`. Con la migración de sucursales sin corregir falla con el mismo `Invalid column name 'BranchId'`.

Pendientes detectados en la Fase 3:

- Timbrado con un PAC y certificados CSD, factura global, complementos de pago y notas de crédito.
- Las opciones de un producto no consumen insumos propios; falta dividir cuentas y unir mesas (cancelar un platillo de una ronda enviada se resolvió en la Fase 15); las mesas son del negocio, no de la sucursal.
- Menú en línea sin pago en línea, entrega a domicilio ni aviso automático de confirmación.
- Cada ubicación de inventario se volvió sucursal: las que solo eran almacén hay que desactivarlas a mano después de aplicar la migración.
- Los pagos de planes los registra la plataforma a mano; falta el adaptador de un proveedor de pagos con verificación de firma.
- Sin conexión no se puede recargar la página ni iniciar sesión (no hay service worker y el token vive en memoria); la cocina no ve la venta hasta que se envía, y una venta cuyo precio cambió mientras tanto se rechaza y hay que registrarla de nuevo.
- Con EF Core 10, el script idempotente desde una base vacía falla en una migración de febrero ya aplicada en producción; una base nueva se crea con `dotnet ef database update`.

## Resuelto el 2026-10-04 (Fase 4: cerrar el diagnóstico)

- Rol por negocio: el rol que cuenta en un negocio sale de la membresía. El token lleva el rol efectivo, administrar el negocio se lee de la membresía en cada petición y `/me/access` devuelve el rol actual; ser dueño de otro negocio ya no da permisos donde la persona es empleada (migración `AlignTenantAdminMemberships` para no quitarle el acceso a quien hoy administra su negocio).
- Equipo: el dueño no se puede desactivar ni cambiar de rol, nadie se quita a sí mismo el rol de administrador y siempre queda un administrador activo, contado por membresía; editar a una persona que tiene otro negocio ya no cambia el rol de su cuenta.
- Avisos de pedidos: política por negocio (migración `AddOrderNotificationPolicy`); por defecto las ventas de mostrador ya no mandan plantillas pagadas, el cliente recibe un solo aviso al crear su pedido y un pedido que espera al negocio no se anuncia como confirmado.
- Al iniciar sesión la app abre la página de entrada de cada rol (Inicio solo para administradores).
- `TenantFilterBypassAllowlistTests` inventaría cada uso de `IgnoreQueryFilters()` por archivo, cantidad y motivo.
- `develop` se retira: `master` es la única rama permanente y el CI y la documentación ya no la mencionan. Borrar la rama en GitHub es un paso manual del dueño del repositorio.
- Los tipos generados del cliente marcaban como obligatorios los enums y objetos que pueden faltar (`OrderStatus?`, `PlanDto?`); el filtro de OpenAPI ya respeta la nulabilidad de C#.
- Cocina y Pedidos suenan con comandas nuevas y pedidos en línea por confirmar; Pedidos ya no parpadea con los eventos en vivo y recarga al reconectar.
- Pantalla de error con recarga automática ante archivos de una versión anterior (antes la app quedaba en blanco después de un despliegue).
- Registro corto (datos fiscales y dirección opcionales y plegados), MXN por defecto (migración `UseMexicanPesosByDefault`), diálogo propio para abrir una cuenta sin mesa.
- Corte de caja imprimible por turno y del día; abrir caja desde Vender.
- Logs por OpenTelemetry con negocio y usuario por id.
- Acentos en 123 textos de la interfaz y 112 mensajes del servidor; la palabra "tenant" sale de los mensajes y de Equipo.
- "Abrir otro negocio" con la misma cuenta; Más vendidos cuenta lo cobrado; un teléfono inválido en un pedido de cliente responde 400 en español.
- Taller: insignias de prioridad (Urgente, Baja) y de entrega en palabras ("Atrasada 2 h", "Entrega en 35 min"), prioridad al recibir la pieza y detalle con fotos, lista de revisión y el resto del pedido.
- Agenda: abre con hoy, reprograma (avisa al cliente), liga el servicio del catálogo y a la persona del equipo, y no deja dos citas traslapadas a la misma persona (migración `AddAppointmentResourcesAndStockPolicy`).
- Categorías: archivar pasa los productos a otra categoría y se pueden restaurar.
- Compras: detalle de la orden, recepción parcial con el costo de la factura, enviar un borrador y cancelar mientras no llegue nada.
- Política de existencias por negocio: bloquear la venta nombrando el insumo que falta (por defecto) o vender y avisar al cajero.
- Respuestas automáticas por palabras completas sin acentos, en pausa cuando una persona atiende el chat, sin preguntas en cada respuesta y con enlace al menú en línea; sin campos de IA en su pantalla.
- Chat: palomitas reales (se guarda el `wamid`) en vivo, marcadores para fotos, notas de voz y ubicaciones, y el mismo hilo para `+52` y `+521`.
- Clientes con teléfono normalizado, notas y direcciones de entrega (migración `AddCustomerAddressesAndChatPause`), visibles en la ficha del chat; la cuenta de cliente ve sus pedidos de todos los negocios en "Mis pedidos".
- Equipo por invitación: la persona elige su contraseña desde un enlace (correo o WhatsApp) que vale 7 días y sirve una vez; las invitaciones pendientes ocupan lugar en el plan y se reenvían o cancelan desde Equipo.
- Roles con nombre (Gerente, Cajero, Mesero, Cocina, Técnico) y permiso de cobro separado (`ChargeOrders`): el mesero toma pedidos y la caja los cobra; la migración `AddChargePermissionAndSmsCooldown` conserva el cobro de quien ya lo hacía. "Sin acceso" se guarda a propósito y ya no se confunde con "los del rol".
- Permisos que se ofrecían al personal y no funcionaban (categorías, borrar productos, respuestas automáticas) ya funcionan; "Equipo" se quitó de los permisos del personal.
- Recuperar la contraseña por SMS al teléfono confirmado; el teléfono solo cambia con la contraseña y un código al número nuevo (el admin ya no lo cambia) y el número mal escrito al registrarse se corrige con la contraseña.
- Bloqueo por cuenta (5 intentos, 15 minutos) en login y códigos, un código SMS por minuto por cuenta, mensajes de Identity en español y reintento con espera al renovar la sesión ante 429.

## Resuelto el 2026-10-05 (Fase 5: un solo modelo, un solo lugar)

- Existencias de producto en el libro mayor de inventario: un producto que se vende tal cual controla sus existencias con un artículo de inventario propio, y vender, cancelar, devolver, comprar y ajustar lo mueven con las mismas reglas que a un insumo (migración `AddProductStockItems`). `Product.StockQuantity` ya no se edita.
- La autorización vive solo en cada caso de uso: el middleware resuelve negocio y membresía, los controllers no tienen roles, los hubs deciden por membresía y la cocina y el mesero tienen sus propios comandos (`MoveKitchenOrderCommand`, `MarkRoundServedCommand`).
- Un negocio siempre vende algo: productos, servicios o alimentos (no se pueden apagar los tres módulos de venta).
- Pruebas del cliente en CI y antes de desplegar; dinero, costos unitarios y existencias con un solo formato; Node.js 24 LTS en CI, despliegue y Docker.
- Errores de enlace de modelos y de FluentValidation en español (`SpanishModelBinding`).
- Modo enfocado para Vender y Cocina: pantalla completa sin menú, barra con reloj y salida, recordado por dispositivo.
- Versiones de NuGet centralizadas en `Directory.Packages.props` (Central Package Management).
- Azure SignalR Service opcional para varias instancias (`Azure__SignalR__ConnectionString`, ver [despliegue](../development/deploy-azure.md)).
- Orden de compra al proveedor por WhatsApp (con el teléfono del proveedor y la unidad de cada insumo; reenviarla pide solo lo pendiente) y corte de caja por WhatsApp desde Mi caja al cerrar, Turnos y Corte del día.

## Resuelto el 2026-10-05 (Fase 6: la propuesta de pantallas y la columna asíncrona completa)

- Menú de la propuesta: Catálogo, Inventario, Clientes y Reportes son secciones con pestañas bajo el título, Configuración tiene submenú y la búsqueda global encuentra cada pestaña.
- Avisos reales en la campana y "Requiere atención" en Inicio (`GET /api/v1/dashboard/attention`): pedidos en línea por confirmar, insumos bajos, cajas abiertas desde ayer, mensajes que fallaron, chats sin leer, servicios atrasados y citas por confirmar, según los permisos de cada quien. El estado de la caja está siempre a la vista en el encabezado.
- Fechas, "hoy" y los rangos de los reportes en la zona horaria del negocio, que se elige en Mi negocio (cinco zonas de México).
- La búsqueda de Inventario (movimientos y kardex), Compras, Mi caja y Actividad va al servidor y recorre todas las páginas.
- Compras: el costo del insumo se precarga al agregar la línea, y una compra que llegó en parte se puede cerrar sin esperar el resto, con motivo y lo que no llegó (migración `AddPurchaseOrderClosing`).
- Accesibilidad verificada por el lint (`eslint-plugin-jsx-a11y`): etiquetas ligadas a su campo y controles con nombre. WhatsApp sin jerga en la configuración.
- Avisos de citas por la cola de mensajería, guardados con el cambio y enviados con reintentos; un solo cliente de la API de Meta (`WhatsAppCloudClient`) para los mensajes de cada negocio y los códigos de la plataforma; alcance de negocio explícito (`BackgroundScopes`) en todos los workers y en el webhook, que procesa cada parte de la entrega con el filtro de su negocio.
- Registrar un movimiento en la caja de otra persona exige siempre ser dueño o administrador: el caso de uso ya no omite la revisión si le faltan dependencias.

## Resuelto el 2026-10-05 (Fase 7: contrato, capas y entrega)

- Enums con nombre en el contrato (`x-enum-varnames`, `x-enum-flags`) y generados en el cliente (`src/api/generated/enums.ts`, `npm run api:types`); `types/index.ts` ya no copia ninguno y CI revisa que el archivo esté al día.
- Reglas de negocio de Infrastructure a Application: respuestas automáticas, búsqueda y reclamo de clientes temporales y los estados de entrega de WhatsApp (`ApplyWhatsAppStatusesCommand`, en el alcance del negocio dueño del número). Infrastructure solo adapta (Meta, enlaces de la app, correo).
- Inventario dividido por pestaña y diálogo (de 2,494 líneas a archivos de 60–360). De paso se corrigieron:
  - la unidad de las existencias de productos vendidos por rebanada, libra, onza, par, rollo, ración, sesión o servicio, que se mostraba como número;
  - los errores de los diálogos, que salían detrás del diálogo;
  - la búsqueda, que se perdía al cambiar de pestaña;
  - las recargas de más al elegir otra receta.
- Despliegue que verifica el esquema: `/health/schema` responde 503 si falta en la base una migración de la versión y la prueba de humo lo exige. Slot de staging con swap y aprobación del environment `production`, opcional.
- Lealtad: decisión explícita ([ADR 0009](../adr/0009-loyalty-per-business.md)); se quitó el método que escribía los puntos globales.

## Resuelto el 2026-10-05 (Fase 9: cuentas, archivos y la suite entre negocios)

- Rol por defecto sin privilegios (`UserRole.None`), sin migración de datos.
- Login sin enumerar cuentas ni servir de oráculo del bloqueo, y contraseñas de al menos 8 caracteres que no sean comunes ni contengan el correo o el nombre.
- Conexiones SignalR que se cierran al terminar su sesión o perder el acceso (`HubAccessSweeper`).
- Subidas con carpeta y nombre validados, avatares por cuenta y la foto anterior de un producto borrada solo cuando la nueva quedó guardada. El procedimiento `20261005_StorageUrlsOutsideTheirFolder` limpia las fotos de producto con la dirección de otro negocio.
- La suite entre negocios cubre inventario, compras, caja, citas, los hubs y el webhook de WhatsApp.
- Los refresh tokens se buscan solo por su hash; `Token` y `ReplacedByToken` quedan sin usar hasta que se borren.

## Resuelto el 2026-10-06 (Fase 10: limpieza del token, códigos por SMS, logs y auditoría de soporte)

- `RefreshTokens.Token`, `ReplacedByToken` e `IX_RefreshTokens_Token` borrados (`DropPlainRefreshTokenColumns`, escrita a mano); su rollback los crea otra vez, vacíos, para poder volver a la Fase 8.
- Restablecer la contraseña por SMS sin oráculo de la cuenta ni del bloqueo, y los códigos anónimos enviados después de responder; el reenvío de la confirmación respeta el minuto entre códigos.
- Los 4xx esperados como advertencia en el log, no como error.
- La suite entre negocios cubre mesas, sucursales, equipo, campañas y reportes.
- El acceso de soporte de `SUPER_ADMIN` a otro negocio queda en la actividad de ese negocio.
- Las actions de los workflows ya no apuntan a Node 20 (`checkout`, `setup-dotnet` y `setup-node` v5; `upload-artifact` v6).

## Resuelto el 2026-10-06 (Fase 11: códigos por el outbox, recepción idempotente, cancelación y suite)

- Los códigos por SMS sin sesión pasan por el outbox: la petición hace lo mismo exista o no la cuenta y un reinicio no los pierde; un fallo del proveedor devuelve el minuto y se reintenta.
- La recepción de compra con llave de idempotencia se registra una vez, también cuando dos copias llegan a la vez (pruebas contra SQL Server).
- La suite entre negocios cubre Inicio (métricas y "Requiere atención"), cocina, tablero de servicios y agentes de IA.
- Los controllers pasan el `CancellationToken`: las lecturas se detienen cuando el cliente se va y los cambios terminan; una lectura abandonada responde 499 y queda como Information en el log.

## Resuelto el 2026-10-06 (Fase 12: suite por área, listados acotados, columnas sin uso y actions por SHA)

- La suite entre negocios cubre reembolsos y anulaciones, recetas (sin productos, insumos ni almacenes del otro, y sin dejar filas al rechazar) y la promoción rápida (solo a sus compradores, sin tocar la plantilla del otro). (Se dijo entonces que cada controller con datos del negocio tenía prueba en ella; la Fase 13 encontró que faltaban `/api/v1/me` y `/api/v1/tenants`.)
- Categorías, almacenes y agentes de IA tienen un tope por negocio, así que sus listados completos se mantienen chicos; una plantilla de categorías se instala completa o no se instala. La cocina lee los 200 pedidos y las 200 comandas más recientes.
- Las consultas que incluyen colecciones las leen en sentencias separadas, en lugar de un join que repite la fila principal por cada combinación.
- `Product.StockQuantity`, `LowStockThreshold`, `CustomerProfile.LoyaltyPoints` y `LoyaltyTier` salen del modelo y del contrato; la tarjeta de existencias ya no propone lo capturado a mano. Es el primer paso: las columnas siguen en la base.
- Cada action de los workflows se fija por el SHA de su versión y CI rechaza una etiqueta; `azure/login` pasa a v3.1.0, en Node 24.

## Resuelto el 2026-10-06 (Fase 13: columnas borradas, imágenes por digest y cobertura de la suite)

- `Products.StockQuantity`, `LowStockThreshold` y `CustomerProfiles.LoyaltyPoints` y `LoyaltyTier` borradas (`DropStockAndLoyaltyColumns`, escrita a mano); su rollback las crea otra vez, vacías, para poder volver a la Fase 11.
- Las imágenes de Docker de CI, del `Dockerfile` y de `docker-compose` van con su etiqueta exacta y su digest, y CI rechaza una action o una imagen sin fijar (`.github/scripts/check-pins.py`).
- `CrossTenantSuiteCoverageTests` exige que cada controller con datos del negocio entre a la suite entre negocios; las rutas de la cuenta y la lista de negocios ya están en ella.

## Resuelto el 2026-10-09 (columna `RequireOpenCashSession`)

- `TenantSettings.RequireOpenCashSession`, de la política que pedía caja solo para cobrar en efectivo, se borró en dos pasos: la PR #47 y la Fase 15 dejaron de mapearla (las dos `StopMappingRequireOpenCashSession`) y `DropRequireOpenCashSession` (escrita a mano) la borra. Su rollback la crea otra vez en 0 para volver a una versión anterior; los negocios que la tenían activada solo quedan en el respaldo.

## Resuelto el 2026-10-10 (prueba de humo por commit)

- La prueba de humo reconocía la versión nueva solo por el nombre del script del cliente, así que un despliegue que solo cambiaba el backend (como el de la PR #48) la podía pasar con la instancia anterior, incluso sin aplicar su `apply.sql`. Ahora los health checks nombran el commit de la versión (`BuildInfo`), el publish lo fija y la prueba exige el desplegado en `/health/ready` y `/health/schema`.

## Decisiones humanas pendientes

- Quién confirma con cada tenant los movimientos de inventario desfasados y si se revalúa el costo promedio que alteraron.
- Modelo de autorización definitivo: policies/resource handlers y alcance administrativo cross-tenant (el rol por negocio en el token ya existe desde la Fase 4).
- Flujo administrativo y auditado para alta, promoción y baja de `SUPER_ADMIN`; hoy solo existe el seed de Development.
- Responsable legal-contable de la política fiscal (descuentos, IEPS, retenciones y CFDI).
- Cuentas por cobrar, saldo a favor y Wallet (los reembolsos ya existen; los ajustes contables no).
- Cuándo activar el slot de staging (plan Standard) y quién aprueba los despliegues a producción.
- Cantidades fraccionarias y control de stock por producto.
- Capabilities ligadas al plan contratado.
- Evidencia de servicios: si el cliente recibe las fotos (WhatsApp, ticket o seguimiento) y si se guardan con un plazo; la Fase 25 decidió fotos por orden, solo para el negocio, mientras exista la orden ([ADR 0024](../adr/0024-service-photos.md)).
