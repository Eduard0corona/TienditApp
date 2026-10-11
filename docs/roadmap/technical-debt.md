# Deuda técnica

Actualizada el 2026-10-09 tras las Fases 0 a 17 del roadmap de producto (la auditoría y sus correcciones son del 2026-10-01).

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
- Activación por correo (2026-10-06): el registro de negocio y de cliente ya no abre sesión ni manda código por SMS; la cuenta se activa con el enlace del correo y después inicia sesión ([identidad](../architecture/identity-permissions-capabilities.md)). Pendiente:
   - sin un proveedor de correo configurado (`EmailSettings`) nadie que se registre puede activar su cuenta. Los valores de ejemplo de `appsettings.json` (`YOUR_SMTP_USERNAME`) no cuentan como vacíos: el servicio intenta enviar con ellos, falla y solo lo registra en el log;
   - el registro todavía responde "Ya existe una cuenta con este correo", así que dice qué correos tienen cuenta;
   - el envío del correo no pasa por el outbox: si el proveedor falla, la cuenta queda creada y la persona pide un enlace nuevo desde "Revisa tu correo".

## Media

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
- Todo cobro y reembolso exige una caja abierta que lo tome, así que ya no se guardan pagos sin movimiento de caja (los guardados antes siguen fuera de los turnos).
- “AI Agent” conserva campos de modelo/temperatura/memoria aunque la implementación es por palabras clave, lo que puede inducir a error.
- Un negocio sin plan (todos los anteriores a la Fase 3) puede activar cualquier módulo desde `PUT /auth/my-tenant`; con plan, solo los incluidos.
- Las actions fijadas por SHA y las imágenes de Docker fijadas por digest no se actualizan solas: no hay Dependabot, y los procedimientos están en [despliegue](../development/deploy-azure.md#columnas-borradas-imágenes-por-digest-y-cobertura-de-la-suite-fase-13). Una imagen fijada no recibe los parches de seguridad de su etiqueta hasta que alguien cambia el digest.
- Cocina por producto: un negocio que no es de comida y activa el módulo de cocina no ve comandas, porque la cocina toma los pedidos por giro y no por producto.
- Resuelto en la Fase 16: los dueños eligen el tema de su negocio entre ocho paletas curadas (Configuración › Negocio › Apariencia, [ADR 0010](../adr/0010-business-themes.md)). Su equipo ve la app con esos colores y sus clientes, el menú en línea; cada par de colores pasa WCAG AA en claro y en oscuro, y una prueba lo exige.
  - Paleta: los colores de estado de Hyper (verde 2.0:1, rojo 3.0:1, amarillo 1.6:1, azul 2.5:1 con texto blanco) se cambiaron por unos AA en los dos modos; el texto general sube de 4.28:1 a 7:1 sobre la página.
  - Modo oscuro: tiene fondo oscuro (antes la página se quedaba clara detrás de tarjetas oscuras), con alertas, chips, chat de WhatsApp y fichas en versión oscura.
  - Consistencia: Cobrar usa el color principal en Vender, Mesas y Pedidos, y ningún estado se pinta con el acento.
  - Pendiente:
    - Hyper sigue cargado (436 KB de `app.min.css`), con colores compilados que `theme.css` cubre uno por uno; los 60 KB de `vendor.min.css` sin uso salieron en la Fase 20B.
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
  - Pendiente (20C a 20F): un layout propio, las clases que hoy solo da Hyper, Bootstrap 5.3.8 compilado con los valores actuales en lugar de `app.min.css` y los arreglos visibles (foco del teclado, contraste del modo oscuro, campos de 16 px en el teléfono). Los tipos de los datos simulados de las escenas no se comprueban contra el contrato: baseline y comparación usan los mismos, así que no esconden una regresión visual, pero pueden alejarse de la API real.
- Resuelto en la Fase 19: el menú en línea vende ([ADR 0011](../adr/0011-opening-hours.md)).
  - El negocio captura su horario (hasta dos por día) y el menú dice si está abierto, en la hora del negocio. Fuera de horario no recibe pedidos para recoger o a domicilio, y el servidor también los rechaza; los de una mesa sí llegan.
  - El encabezado del menú tiene logo o inicial, el horario de la semana, la dirección de la sucursal principal (nunca la fiscal) con "Cómo llegar" y WhatsApp.
  - Los productos sin foto muestran su inicial cuando otros tienen foto, en el menú y en Vender.
  - El logo del negocio sale en el selector de negocio y en el ticket.
  - Las filas de categorías se desvanecen en el borde que esconde más.
  - Pendiente:
    - No hay días festivos ni cierres especiales.
    - El horario es del negocio, no de cada sucursal.
    - No hay pedidos programados para cuando abra.
    - Las respuestas automáticas de WhatsApp todavía no dicen el horario.
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
- El tema visual sigue siendo el CSS de Hyper (fase 1: tema propio; desde la Fase 16, `theme.css` por capas con el tema de cada negocio encima de Hyper, que sigue pendiente de retirar).

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

## Decisiones humanas pendientes

- Quién confirma con cada tenant los movimientos de inventario desfasados y si se revalúa el costo promedio que alteraron.
- Modelo de autorización definitivo: policies/resource handlers y alcance administrativo cross-tenant (el rol por negocio en el token ya existe desde la Fase 4).
- Flujo administrativo y auditado para alta, promoción y baja de `SUPER_ADMIN`; hoy solo existe el seed de Development.
- Responsable legal-contable de la política fiscal (descuentos, IEPS, retenciones y CFDI).
- Cuentas por cobrar, saldo a favor y Wallet (los reembolsos ya existen; los ajustes contables no).
- Cuándo activar el slot de staging (plan Standard) y quién aprueba los despliegues a producción.
- Cantidades fraccionarias y control de stock por producto.
- Capabilities ligadas al plan contratado.
