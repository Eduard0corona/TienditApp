# Despliegue en Azure App Service

Cada push a `master` corre el workflow `Deploy WebApp (API + SPA)` (`.github/workflows/deploy-azure-webapp.yml`): compila el cliente y la solución, corre las pruebas, publica la API con la SPA en `wwwroot`, despliega el paquete y espera a que la app responda con esa versión. Las migraciones no corren al desplegar: antes del merge se aplica `database/production/<MigrationId>/apply.sql` (ver [migraciones](database-migrations.md)). Sin más configuración el paquete va directo a la app; con un [slot de staging](#slot-de-staging-con-swap-y-aprobación) va primero al slot y pasa a producción con un swap aprobado.

## Prueba de humo

Después de desplegar, `.github/scripts/smoke-test.sh` consulta la app hasta 30 veces, cada 20 segundos:

- las respuestas de `GET /health/ready` y `GET /health/schema` deben nombrar el commit desplegado (`"commit"`). El publish lo sella en la versión del ensamblado (`-p:SourceRevisionId=${{ github.sha }}`, que queda como `1.0.0+<sha>`) y la API lo reporta en sus health checks. Así no pasa una instancia de la versión anterior, ni siquiera cuando el cliente no cambió y su script conserva el nombre: cada chequeo de abajo lo contesta la versión nueva;
- `GET /health/ready` debe responder 200 (la API arrancó y llega a la base de datos);
- desde la Fase 21, esa respuesta debe decir `"business-time-zones":"Healthy"`: las zonas horarias que un negocio puede elegir existen en el servidor. Si una cae en UTC, el chequeo queda Degraded (200, la app sigue atendiendo) y el workflow falla diciéndolo; el log stream nombra las zonas ([observabilidad](../architecture/observability.md));
- desde la Fase 23A, si esa respuesta no dice `"email-provider":"Healthy"` el workflow pasa con un aviso, sin fallar: no hay proveedor de correo o tiene los valores de ejemplo de `appsettings.json`, así que nadie que se registre puede activar su cuenta y no salen la recuperación de contraseña ni las invitaciones por correo. Se configura en el App Service con `EmailSettings__Provider` (`Smtp`), `EmailSettings__FromEmail`, `EmailSettings__FromName` y `EmailSettings__Smtp__Host`, `__Port`, `__Username`, `__Password` y `__EnableSsl`; el log del chequeo dice qué falta, sin valores;
- `GET /health/schema` debe decir `Healthy`: la base tiene todas las migraciones de esta versión. Si falta un `apply.sql`, dice `Unhealthy` (503) y el workflow falla pidiendo aplicarlo; la app sigue atendiendo, porque ese chequeo no forma parte de `/health/ready`;
- `GET /` debe servir el `index.html` recién compilado (sus scripts llevan el hash del build del cliente, que solo cambia si cambió el cliente); desde la Fase 24D-2 lo sirve con el encabezado de Explorar;
- desde la Fase 20, ese script, pedido como lo pide un navegador (`Accept-Encoding: br, gzip`), debe llegar con `Content-Encoding: br` y `Cache-Control: ... immutable` (ver [entrega del cliente](#entrega-del-cliente-comprimida-y-en-caché)).

Si en 10 minutos no se cumplen las seis, el workflow falla con el último estado de cada consulta y el commit que respondió. Si respondió otro commit, la versión nueva no arrancó; el aviso de migraciones faltantes solo sale cuando lo dice la versión nueva, porque la anterior no conoce sus migraciones. Sin slot, la versión ya quedó publicada: revisa el log stream del App Service (arranque, cadena de conexión, secretos; el chequeo del esquema escribe ahí qué migración falta) y aplica el `apply.sql` pendiente. La variable de repositorio `SMOKE_TEST_URL` cambia la dirección de producción consultada (un dominio propio); sin ella se usa la que devuelve el despliegue (`https://<app>.azurewebsites.net`).

## Entrega del cliente comprimida y en caché

Desde la Fase 20 la API entrega el cliente así (`src/FoodParkDigital.WebApi/Http/ClientDelivery.cs`, probado en `ClientDeliveryTests`):

- **Copias comprimidas del publish.** Al publicar, el SDK de .NET deja junto a cada archivo de `client/dist` que se comprime (scripts, estilos, `index.html` y las imágenes SVG) una copia Brotli (`.br`, a su máxima calidad) y una gzip (`.gz`): es la compresión de static web assets, que el csproj deja encendida (`CompressionEnabled`). Para los archivos de `/assets`, la API manda la copia que el navegador acepta (Brotli primero) como el archivo que codifica, con `Content-Encoding` y `Vary: Accept-Encoding`; no comprime nada en cada petición, así que no gasta CPU. Ninguna copia se sirve por su nombre: `/assets/x.js.br` o `/index.html.gz` responden 404.
- **Solo `/assets` se comprime.** Las respuestas de la API llevan datos de una sesión y nunca se comprimen: su tamaño no sirve para adivinarlos (BREACH).
- **Caché.** Los archivos de `/assets` llevan el hash de su contenido en el nombre, así que una versión nueva nunca reusa un nombre: van con `Cache-Control: public, max-age=31536000, immutable` y el navegador no vuelve a preguntar por ellos. Solo un archivo que sale (200, 206) o se confirma (304) lleva esa caché y la codificación: una condición o un rango que fallan (412, 416) no se guardan en ninguna caché, que los serviría en lugar del script. `index.html`, que sirve cualquier ruta de la app, va con `no-cache`: el navegador lo revisa en cada visita y un despliegue se ve de inmediato.
- **Archivos que ya no existen.** Un archivo de `/assets` que no está responde 404, nunca el `index.html`: una página abierta antes del despliegue lo nota y carga la versión nueva (`utils/chunkReload.ts`). Las pantallas del negocio (con sesión) abiertas que no piden nada nuevo ven el aviso "Hay una versión nueva de TienditApp" (`NewVersionNotice`), que revisa el `index.html` al volver a la pestaña, al recuperar la conexión y cada 10 minutos. El menú en línea y el inicio de sesión no lo muestran: se abren de nuevo en cada visita.

Medido en Chromium con tamaño de teléfono sobre el menú en línea (sin fotos): la primera visita bajó de 1,274,580 B a 272,273 B; el CSS global, de 655 KB a 72 KB, y React, de 286 KB a 81 KB. La fuente de iconos (`woff2`, 189 KB) ya viene comprimida y queda igual.

## Credenciales: del publish profile a OIDC

Hoy el workflow puede desplegar con el secreto `AZURE_WEBAPP_PUBLISH_PROFILE`, un archivo con usuario y contraseña de larga duración. OIDC lo reemplaza por una credencial federada: GitHub pide a Microsoft Entra un token que vale solo para esta ejecución, y no queda ningún secreto que pueda filtrarse o caducar.

El workflow ya soporta ambos caminos: si existen las variables de repositorio de OIDC, inicia sesión con `azure/login` y despliega sin publish profile; si no, usa el publish profile como antes. Para cambiar:

1. **Identidad.** En el portal de Azure, crea una identidad administrada asignada por el usuario (o un registro de aplicación de Entra) para el despliegue, por ejemplo `tienditapp-deploy`.
2. **Credencial federada.** En esa identidad: *Federated credentials* → *Add* → escenario *GitHub Actions deploying Azure resources*, organización `Eduard0corona`, repositorio `TienditApp`, entidad *Branch*, rama `master`. El sujeto queda `repo:Eduard0corona/TienditApp:ref:refs/heads/master`: solo un workflow que corre en `master` puede usarla.
3. **Permiso mínimo.** En el App Service: *Access control (IAM)* → *Add role assignment* → rol *Website Contributor* para esa identidad, con alcance solo en esa app (no en el grupo de recursos ni en la suscripción).
4. **Variables en GitHub.** En el repositorio: *Settings* → *Secrets and variables* → *Actions* → pestaña *Variables*: `AZURE_CLIENT_ID` (id de cliente de la identidad), `AZURE_TENANT_ID` (directorio) y `AZURE_SUBSCRIPTION_ID`. No son secretos. `AZURE_WEBAPP_NAME` sigue como secreto.
5. **Probar.** Corre el workflow a mano desde `master` (*Actions* → *Deploy WebApp* → *Run workflow*). Debe aparecer el paso `Sign in to Azure (OIDC)` y pasar la prueba de humo.
6. **Retirar el publish profile.** Borra el secreto `AZURE_WEBAPP_PUBLISH_PROFILE` y, en el App Service, *Configuration* → *General settings*: desactiva *SCM Basic Auth Publishing Credentials* y *FTP Basic Auth Publishing Credentials*. Así el publish profile anterior deja de servir aunque alguien lo tenga.

Para volver al publish profile basta con borrar `AZURE_CLIENT_ID` y restaurar el secreto (y la autenticación básica del sitio).

## Varias instancias: actualizaciones en vivo con Azure SignalR

Cocina, pedidos, caja y chat se actualizan en vivo por SignalR (`/hubs`). Sin configuración extra los hubs corren dentro de la app: con una sola instancia basta. Si el App Service escala a varias instancias, cada navegador queda conectado a una de ellas y no recibe lo que publican las demás (las pantallas se ponen al día con su sondeo de respaldo, pero tarde).

Para escalar se usa Azure SignalR Service, que reparte los mensajes entre instancias sin un backplane propio:

1. Crea un recurso *SignalR Service* en modo de servicio **Default** (el plan *Free* sirve para probar; *Standard* para producción).
2. Copia su cadena de conexión (*Keys* → *Connection string*).
3. En el App Service: *Configuration* → *Application settings* → `Azure__SignalR__ConnectionString` con esa cadena (o una referencia a Key Vault). Es un secreto: no va en `appsettings.json` ni en el repositorio.
4. Reinicia la app. Con el ajuste presente, `Program.cs` llama a `AddAzureSignalR()`; sin él, todo sigue dentro del proceso. Los hubs, sus grupos por negocio y la autenticación con el token no cambian: la app valida el token al negociar y el servicio solo transporta los mensajes.

Para regresar basta con borrar el ajuste y reiniciar. La afinidad de sesión (*ARR affinity*) no hace falta para SignalR cuando se usa el servicio.

## Archivos privados: comprobantes y fotos de servicio

Desde la Fase 8 los comprobantes de transferencia y las fotos de servicio no se publican: la API los guarda en un almacenamiento privado y solo los entrega a miembros del negocio (`GET /api/v1/files/{key}`). Las imágenes del catálogo, logos y avatares siguen en el almacenamiento público. Antes del primer despliegue, revisa el proveedor del App Service (`FileStorage__Provider`):

- **Azure Blob.** La app crea el contenedor `private-files` sin lectura anónima la primera vez que guarda un archivo privado (otro nombre con `FileStorage__AzureBlob__PrivateContainerName`, distinto de `ContainerName`). La cadena de conexión necesita crear contenedores y leer su nivel de acceso: la clave de la cuenta puede; con un SAS, crea el contenedor a mano como *Private (no anonymous access)* y dale acceso a los dos contenedores. Si el contenedor privado permite lectura anónima, la API se niega a guardar en él y la subida falla hasta cambiarlo a *Private*.
- **Local.** Por defecto los archivos van a `App_Data/private-files` dentro de la carpeta del sitio. En App Service conviene una carpeta persistente fuera del paquete, para que ningún despliegue la toque: `FileStorage__Local__PrivateRootPath` = `/home/data/private-files` (Linux) o `D:\home\data\private-files` (Windows). La app no arranca si la carpeta está dentro de `wwwroot`.

Al arrancar con `BackgroundJobs__Enabled` (no en el slot de staging), la app mueve una sola vez los comprobantes y las fotos que estaban en el almacenamiento público al privado, y registra en el log cuántos movió (`Moved {Count} private files out of the public storage`). Con Azure Blob esto importa: hasta moverse, un archivo antiguo se sigue descargando desde la URL pública del blob. Mientras no se mueva, o si el movimiento falla (queda un error en el log y se reintenta en el siguiente arranque), la API lo lee donde esté; `/uploads/orders` responde 404 desde el despliegue.

Si hubiera que volver a la versión anterior, los archivos no se pierden, pero esa versión solo los muestra desde el almacenamiento público: copia `orders/` del privado al público (`azcopy copy` entre contenedores, o de `PrivateRootPath` a `wwwroot/uploads`). Los pagos y servicios registrados después guardan `/files/...`, que la versión anterior no muestra.

## Sesiones y aislamiento (Fase 8)

El `apply.sql` de la Fase 8 se aplica, como siempre, mientras corre la versión anterior, y ella sigue funcionando: escribe sus refresh tokens en texto (cada uno con una sesión propia) y agrega partidas y cambios de estado sin negocio. Al arrancar, la versión nueva:

- da a esas partidas y cambios de estado el negocio de su orden (`OrderTenantBackfillService`, en cada arranque y también en el slot de staging; no hace nada cuando no hay pendientes). Hasta entonces el filtro por negocio los oculta;
- con `BackgroundJobs__Enabled` (no en el slot), convierte a hash los refresh tokens en texto y registra cuántos (`Hashed {Count} refresh tokens stored as plain values`); un token que llega antes se convierte al usarse. La Fase 9 quita ese proceso: su migración convierte lo que quede.

Después del despliegue, cada cliente renueva su sesión una vez sin notarlo, porque los access tokens anteriores no traen sesión. Volver a la versión anterior con un swap funciona sin `rollback.sql`: esa versión sigue leyendo todas las filas (la nueva deja vacía, nunca nula, la columna del valor del token); las sesiones abiertas por la versión nueva terminan y cada quien inicia sesión otra vez.

## Cuentas, sesiones y archivos (Fase 9)

El `apply.sql` de la Fase 9 no cambia el esquema: convierte a hash los refresh tokens que aún estén en texto (con la Fase 8 desplegada y sus jobs activos, normalmente ninguno) y se aplica, como siempre, mientras corre la versión anterior. Volver a la Fase 8 funciona sin `rollback.sql`: lee las filas con hash y trata como "sin valor en texto" el `Token` nulo que dejan los tokens nuevos. Las columnas `Token` y `ReplacedByToken` se borran en la Fase 10.

- `Hubs__AccessCheckInterval` (por defecto `00:00:30`): cada cuánto se revisan las conexiones abiertas a los hubs. Una conexión cuya sesión terminó, o que perdió el acceso al negocio o el permiso del hub, se cierra en la siguiente revisión. Cada instancia revisa las conexiones que atiende.
- Las contraseñas nuevas exigen 8 caracteres con mayúscula, minúscula y dígito, y no pueden ser comunes ni contener el correo o el nombre. Las actuales siguen valiendo hasta que se cambien.
- Los avatares nuevos van a `avatars/users/{cuenta}` en el almacenamiento público; los anteriores se quedan donde están y se borran de ahí al cambiarlos.
- Aparte, en cualquier momento: el [procedimiento para las fotos de producto con la dirección de otro negocio](../../database/production/data-repair/20261005_StorageUrlsOutsideTheirFolder/README.md).

## Limpieza del token, códigos por SMS y soporte (Fase 10)

El `apply.sql` de la Fase 10 borra `RefreshTokens.Token`, `ReplacedByToken` y su índice, y se aplica, como siempre, mientras corre la versión anterior: la Fase 9 no los usa y sigue funcionando, y volver a ella no necesita `rollback.sql`. Antes de aplicar, su `verify.sql` debe mostrar solo el índice `IX_RefreshTokens_Token`, ninguna estadística creada a mano ni objeto que use la tabla, y los valores en texto en 0. Volver a la Fase 8 sí exige primero el `rollback.sql` de esa carpeta, que crea otra vez las columnas vacías.

- Los códigos que piden las pantallas sin sesión (recuperar la contraseña por SMS, reenviar la confirmación del teléfono) se envían después de responder. En la Fase 10 salían de una cola en memoria de cada instancia; desde la Fase 11 salen del outbox (sección siguiente).
- Los rechazos esperados (validación, regla de negocio, sin acceso, no encontrado, cambio concurrente) quedan en el log como advertencia; solo los 500 son errores. La alerta de 5xx por negocio de [observabilidad](../architecture/observability.md) no cambia.
- Una petición de un `SUPER_ADMIN` con `X-Tenant-Id` en otro negocio queda en la actividad de ese negocio (acción `SupportAccess`) antes de ejecutarse; si la base no la puede guardar, responde 503.
- Los workflows usan `actions/checkout`, `actions/setup-dotnet` y `actions/setup-node` v5 y `actions/upload-artifact` v6, que corren en Node 24 (desde la Fase 12, fijadas por SHA).

## Códigos por el outbox, recepción idempotente y cancelación (Fase 11)

La Fase 11 no tiene migración ni `apply.sql`, y volver a la Fase 10 no necesita nada.

- Los códigos por SMS que piden las pantallas sin sesión salen del outbox (`VerificationCodeRequestedHandler`), así que necesitan los workers: con `BackgroundJobs__Enabled=false` (por ejemplo el slot de staging) se guardan y salen cuando una instancia con workers los atiende, si no pasaron 10 minutos. Ya no hay cola en memoria por instancia: un reinicio no los pierde.
- Recibir una compra acepta `idempotencyKey`; el cliente nuevo la manda y un doble clic o un reintento se registra una vez. Un cliente anterior, sin llave, se comporta como antes.
- Las lecturas (GET) se detienen cuando su cliente se va y los cambios terminan siempre (`RequestCancellation`); una lectura abandonada queda en el log como Information con 499, no como error.

## Columnas sin uso, listados acotados y actions por SHA (Fase 12)

El `apply.sql` de la Fase 12 no cambia el esquema ni los datos: solo registra `20261006101452_StopMappingStockAndLoyaltyColumns`, y se aplica, como siempre, antes del merge. La versión nueva deja de leer y escribir `Products.StockQuantity`, `LowStockThreshold` y `CustomerProfiles.LoyaltyPoints` y `LoyaltyTier`: las filas que guarda las dejan en NULL y en 0, y la Fase 11 sigue funcionando con ellas, así que volver a ella no necesita `rollback.sql`. La siguiente fase borra las columnas.

- Al empezar a controlar las existencias de un producto, la tarjeta ya no propone como existencia inicial lo capturado a mano: se cuenta.
- Un negocio tiene como máximo 300 categorías, 100 almacenes (contando el de cada sucursal) y 20 agentes de IA, también los archivados o inactivos; crear uno más se rechaza con un mensaje que lo dice. La cocina muestra los 200 pedidos y las 200 comandas más recientes.
- Cada action se fija por el SHA completo del commit de su versión, con la versión como comentario, y el job `Backend` de CI falla si una queda por etiqueta. `azure/login` pasa de v2 (Node 20) a v3.1.0 (Node 24) con las mismas entradas; solo corre con OIDC.
- Para actualizar una action: `git ls-remote --tags https://github.com/<dueño>/<action>` da el commit de cada versión (en una etiqueta anotada, el de la línea que termina en `^{}`). Cambia el SHA y el comentario en cada `uses:` después de leer sus notas de versión; sin Dependabot, nadie lo propone solo.

## Columnas borradas, imágenes por digest y cobertura de la suite (Fase 13)

El `apply.sql` de la Fase 13 borra `Products.StockQuantity`, `LowStockThreshold` y `CustomerProfiles.LoyaltyPoints` (con su valor por omisión) y `LoyaltyTier`, y se aplica, como siempre, antes del merge y con la Fase 12 desplegada: no las mapea, así que sigue funcionando, y volver a ella no necesita `rollback.sql`. Antes de aplicar, su `verify.sql` debe mostrar como única dependencia la restricción del valor por omisión de `LoyaltyPoints`, ninguna estadística creada a mano ni objeto que use las tablas, y cuánto capturado se pierde (solo queda en el respaldo). Volver a la Fase 11 exige antes el `rollback.sql` de esa carpeta, que crea las columnas vacías.

- Las imágenes de Docker van con su etiqueta exacta y su digest: en CI `azurite:3.37.0` y `mssql/server:2022-CU27-ubuntu-22.04` (lo que era `2022-latest`); en el `Dockerfile` `aspnet:10.0.12`, `sdk:10.0.401` y `node:24-alpine` (Node 24.21.0); en `docker-compose.yml` el mismo SQL Server de CI. El paso `Actions and images pinned` del job `Backend` corre `.github/scripts/check-pins.py`, que falla con una action sin SHA completo o una imagen sin digest.
- Para actualizar una imagen: `docker buildx imagetools inspect <imagen>:<etiqueta>` da el digest que tiene hoy esa etiqueta (el de la lista de plataformas, no el de una sola). Cambia la etiqueta y el digest juntos y revisa las notas de versión; una imagen fijada no recibe los parches de su etiqueta hasta que cambies el digest.
- Un controller nuevo con datos del negocio necesita una prueba en la suite entre negocios: `CrossTenantSuiteCoverageTests` falla hasta que una clase `CrossTenant*` llame sus rutas (desde la Fase 14, las de sus acciones).

## Columna `RequireOpenCashSession` sin mapear

El `apply.sql` de `20261009041644_StopMappingRequireOpenCashSession` no cambia el esquema ni los datos: solo registra la migración, y se aplica, como siempre, antes del merge. La versión nueva deja de leer y escribir `TenantSettings.RequireOpenCashSession`, que no tiene efecto desde que todo cobro exige caja: las configuraciones que guarda la dejan en su valor por omisión, 0, y la versión anterior sigue funcionando con ella, así que volver a ella no necesita `rollback.sql`. La siguiente entrega borra la columna y solo se aplica con esta versión desplegada. La Fase 15 hizo el mismo paso en su rama con `20261008074715_StopMappingRequireOpenCashSession`; al fusionarla quedan las dos migraciones, y ninguna cambia la base ([migraciones](database-migrations.md)).

## Inicio, AuthController y rondas (Fase 14)

La Fase 14 no trae migraciones: no hay `apply.sql` que aplicar antes del merge, y el esquema de la base es el de la Fase 13.

- Inicio cuenta en SQL y lee sus ingresos del resumen diario de los reportes. Las cifras son las de antes, salvo lo que se cobre en el último segundo del día, que ahora cuenta como en Reportes. Un periodo de más de 366 días responde 400 con el motivo.
- `AuthController` se dividió en cinco controllers con las mismas rutas. El documento OpenAPI es el mismo al compararlo como JSON; `client/openapi/openapi.json` y `src/api/generated/schema.ts` solo cambian de orden.
- Cada ronda de una cuenta de mesa descuenta sus insumos cuando va a cocina. Las cuentas abiertas al desplegar se cierran bien: sus rondas de antes se descuentan al cerrarlas y las nuevas, al mandarse.
- Volver a la Fase 13 con cuentas abiertas: esa versión descuenta al cerrar todas las rondas de la cuenta, también las que la Fase 14 ya descontó, y al cancelarla no devuelve lo de las rondas. Antes de regresar, cierra o cancela las cuentas abiertas; si alguna se cerró después, corrige sus existencias con un movimiento de entrada.

## Platillos cancelados, RequireOpenCashSession y Vender (Fase 15)

La Fase 15 trae dos migraciones en una sola carpeta, [`database/production/20261008074715_StopMappingRequireOpenCashSession`](../../database/production/20261008074715_StopMappingRequireOpenCashSession): su `apply.sql` se aplica, como siempre, antes del merge (respaldo, `verify.sql`, `apply.sql`, `verify.sql`).

- `AddTabItemCancellations` solo agrega: `OrderItems.CancelledQuantity` y `CancelledAmount` (sin nulos, 0 por omisión) y la tabla `OrderItemCancellations`. La Fase 14 no los mapea, así que sigue funcionando mientras se publica la nueva y sus renglones toman 0 cancelado.
- `StopMappingRequireOpenCashSession` no cambia la base: la versión nueva deja de mapear `TenantSettings.RequireOpenCashSession` (nadie la lee: todo cobro del negocio exige una caja abierta). La columna se queda con su valor por omisión, 0, que toman las filas nuevas; una fase posterior la borra cuando ninguna versión desplegada la mapee. Es el mismo paso que hizo la PR #47 en master (sección anterior).
- `GET /orders/{id}` ahora trae las rondas, las opciones, los reembolsos y las cancelaciones de la cuenta. El contrato solo agrega: la ruta `POST /orders/{id}/item-cancellations` y los campos `cancelledQuantity`, `chargedPrice` e `itemCancellations`.
- Volver a la Fase 14 (con o sin `rollback.sql`) con platillos cancelados en cuentas abiertas: esa versión recalcula el total con todos los renglones al mandar una ronda y cobraría otra vez lo cancelado. Antes de regresar, cierra o cancela esas cuentas; la consulta 4 de `verify.sql` las cuenta. `rollback.sql` borra la tabla y las columnas de cancelaciones (la bitácora conserva cada cancelación con su motivo) y no toca `RequireOpenCashSession`.

## Temas por negocio y colores accesibles (Fase 16)

La Fase 16 trae una migración, [`database/production/20261009043433_AddBusinessTheme`](../../database/production/20261009043433_AddBusinessTheme). Su `apply.sql` es idempotente y también aplica las que falten: las dos de la Fase 15 y la de la PR #47. Para fusionar las Fases 14, 15 y 16 juntas basta esta carpeta: respaldo, `verify.sql`, `apply.sql`, `verify.sql` y luego el merge. Si ya aplicaste el `apply.sql` de la Fase 15, el de la PR #47 o una versión anterior de este, solo agrega lo que falte.

- `AddBusinessTheme` solo agrega `TenantSettings.Theme` (int, sin nulos, 0 por omisión). 0 es Nopal, el verde de TienditApp: ningún negocio cambia de aspecto hasta que su dueño elige otro tema en Configuración › Negocio › Apariencia. La versión anterior no mapea la columna y sus filas nuevas toman el 0.
- El contrato solo agrega: la ruta `PUT /api/v1/auth/my-tenant/theme`, el enum `BusinessTheme` y el campo `theme` en el perfil del negocio y en el menú en línea.
- Sin cambios de datos, la app se ve distinta para todos los negocios:
  - los colores de estado (éxito, aviso, error, información) pasan WCAG AA;
  - el texto general es más oscuro;
  - el modo oscuro ya tiene el fondo oscuro, y en él el verde de Nopal es más claro;
  - Cobrar usa el color principal en todas las pantallas.
- Volver a la versión anterior de `master` (la Fase 13 con la PR #47): antes, cierra o cancela las cuentas abiertas, porque esa versión descontaría otra vez las rondas al cerrarlas y cobraría lo cancelado (secciones de las Fases 14 y 15). El código anterior no mapea las columnas nuevas y no ve las migraciones que no conoce, así que funciona sin `rollback.sql`.
- Para dejar también la base como estaba, aplica el `rollback.sql` de esta carpeta y después el de la Fase 15. El primero borra la columna del tema y con ella la elección de cada negocio (la bitácora conserva cada cambio, `BusinessThemeUpdated`); al aplicar de nuevo, todos vuelven a Nopal. El segundo borra las cancelaciones. La migración de la PR #47 queda registrada.

## Columna `RequireOpenCashSession` borrada

El `apply.sql` de [`20261009071957_DropRequireOpenCashSession`](../../database/production/20261009071957_DropRequireOpenCashSession) borra `TenantSettings.RequireOpenCashSession` con su valor por omisión, y se aplica, como siempre, antes del merge y con `master` desplegado desde la PR #47 (con o sin las Fases 14 a 16): ninguna de esas versiones la mapea, así que sigue funcionando, y volver a ellas no necesita `rollback.sql`. Si a la base le faltan las migraciones de las Fases 15 y 16, también las aplica: revisa antes el `verify.sql` de su carpeta. Antes de aplicar, el `verify.sql` de esta debe mostrar como única dependencia la restricción del valor por omisión, ninguna estadística creada a mano ni objeto que use la tabla, y cuántos negocios tenían activada la política (cuáles, solo queda en el respaldo). Volver a una versión anterior a la PR #47 exige antes el `rollback.sql` de esa carpeta, que crea la columna otra vez en 0; si también aplicas los `rollback.sql` de las Fases 16 y 15, este va primero.

## Pantallas de trabajo sin estorbos (Fase 17)

La Fase 17 solo cambia el cliente: no trae migraciones, scripts ni cambios de contrato, y se fusiona sin tocar la base. Si aún no aplicas el `apply.sql` de la sección anterior, aplícalo antes del merge: lo exige la versión que ya está en `master` (`/health/schema` responde 503 sin él) y también esta. Volver a la versión anterior no necesita nada en la base.

## Listas e Inventario (Fase 18)

La Fase 18 no trae migraciones ni scripts y se fusiona sin tocar la base. El contrato solo agrega el parámetro opcional `channel` a `GET /api/v1/orders` (el enum `OrderChannel`: 0 mostrador, 1 WhatsApp, 2 menú en línea); quien no lo manda recibe la lista de siempre, y un valor que no existe responde 400. Volver a la versión anterior no necesita nada en la base.

## Menú en línea que vende (Fase 19)

La Fase 19 trae una migración, [`database/production/20261010074254_AddOpeningHours`](../../database/production/20261010074254_AddOpeningHours). Su `apply.sql` se aplica, como siempre, antes del merge: respaldo, `verify.sql`, `apply.sql`, `verify.sql` y luego el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica.

- **La migración:** `AddOpeningHours` solo agrega `TenantSettings.OpeningHours` (`nvarchar(1000)`, con nulos, sin valor por omisión), el horario del negocio como lista JSON. Empieza vacía: ningún negocio se ve cerrado ni deja de recibir pedidos hasta que su dueño captura el horario en Configuración › Negocio › Horario ([ADR 0011](../adr/0011-opening-hours.md)).
- **Mientras se publica:** la versión de `master` (Fase 18) no mapea la columna, así que sigue funcionando y sus filas nuevas la dejan en NULL. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y falla al leer la configuración del negocio (`Invalid column name 'OpeningHours'`).
- **El contrato solo agrega:**
  - la ruta `PUT /api/v1/auth/my-tenant/opening-hours` y el enum `DayOfWeek` (0 domingo … 6 sábado);
  - el parámetro opcional `table` en `GET /api/v1/public/menu/{slug}`, la mesa del QR;
  - los campos `address` y `openingHours` del menú en línea, `openingHours` en el perfil del negocio y `logoUrl` en `GET /api/v1/me/tenants`.
- **Cambio de comportamiento:** `ordersPaused` del menú en línea también es `true` fuera de horario; antes solo lo era sin caja abierta. Un navegador con el cliente anterior muestra entonces su aviso "Por ahora no estamos recibiendo pedidos en línea", que también es cierto.
- **Volver a la versión anterior:** no necesita nada en la base. Esa versión no mapea la columna: los horarios se quedan guardados sin efecto y los menús vuelven a recibir pedidos a cualquier hora con una caja abierta.
- **Dejar también la base como estaba:** aplica el `rollback.sql`. Borra la columna y con ella los horarios capturados (la bitácora conserva cada cambio, `OpeningHoursUpdated`); antes, la consulta 3 de `verify.sql` cuenta los negocios que lo perderían.

## Cliente comprimido, aviso de versión y dirección del menú (Fase 20A)

La Fase 20A no trae migraciones ni scripts y se fusiona sin tocar la base. El contrato no cambia de forma: ninguna ruta ni campo nuevo.

- **Entrega del cliente:** comprimida y en caché, como describe [la sección de arriba](#entrega-del-cliente-comprimida-y-en-caché). La prueba de humo exige ahora el script del cliente con `Content-Encoding: br` y `immutable`; si el publish dejara de dejar las copias, la app sigue funcionando sin comprimir y el workflow lo marca.
- **Cambio de comportamiento:** el campo `address` de `GET /api/v1/public/menu/{slug}` es ahora la dirección de la sucursal principal (`Branch.IsDefault`), en una línea y `null` si no tiene calle. En la Fase 19 era la dirección fiscal del negocio (`Tenant.Address`), que suele ser la casa del dueño ([ADR 0011](../adr/0011-opening-hours.md), punto 7).
- **Volver a la versión anterior:** no necesita nada en la base, pero esa versión vuelve a publicar en el menú, con enlace al mapa, la dirección fiscal de cada negocio que la capturó (al registrarse o en Configuración › Negocio) y tiene el menú encendido. Si hay que regresar, avisa a esos negocios o regresa solo lo indispensable. También quita la compresión y la caché larga; los archivos `immutable` no se quedan viejos porque su nombre cambia con cada build.

## Días especiales y horario en WhatsApp (Fase 21)

La Fase 21 trae una migración, [`database/production/20261011161835_AddSpecialDays`](../../database/production/20261011161835_AddSpecialDays). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y luego el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica.

- **La migración:** `AddSpecialDays` solo crea la tabla `SpecialDays`, vacía, con cinco restricciones CHECK y sus dos índices ([ADR 0012](../adr/0012-special-days.md)). No toca `TenantSettings` ni otros datos: ningún negocio cambia su horario ni deja de recibir pedidos hasta que su dueño marca un día especial en Configuración › Negocio › Días especiales. Después de aplicarla, el `verify.sql` muestra sus columnas, las cinco restricciones y los tres índices (la llave, el único y el del periodo).
- **Mientras se publica:** la versión de `master` (Fase 20) no conoce la tabla, así que sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:**
  - las rutas `GET`, `POST`, `PUT` y `DELETE` de `/api/v1/auth/my-tenant/special-days` (`[RequiresTenantManager]`), con sus DTOs y el enum `SpecialDayKind` (0 cerrado, 1 otro horario);
  - campos con nulos en `PublicOpeningHoursDto` del menú en línea: `todayDate`, `hasWeeklyHours`, los textos (`statusLabel`, `statusDetail`, `closedNotice`, `upcomingNotice`), `closedBySpecialDay`, `todayHours`, `opensOn`, `closesOn` y `specialDays`.

  Nada se quita ni cambia de nombre, y el cliente anterior ignora los campos nuevos. Sí cambia lo que traen dos campos que ya existían: `openingHours`, que era `null` sin horario de la semana, llega con `intervals: []` cuando ese negocio tiene un día especial hoy o en los próximos 31 días, y `opensInDays` puede pasar de 7 después de un cierre largo. Una pestaña abierta antes del despliegue, con el cliente anterior, los lee con su redacción hasta que se recarga: a ese negocio le pone "Abierto las 24 horas" ("cierra el jueves a medianoche" en la semana antes del día especial), "Hoy: Cerrado" y la semana cerrada, y en un día especial cerrado dice que abre "a las 0:00"; después de un cierre de más de una semana nombra el día sin la fecha ("abre el martes a las 9:00"). El servidor decide igual al recibir el pedido.
- **Cambios de comportamiento:**
  - **Mesas en un día especial cerrado:** el menú no recibe pedidos, ni desde el QR de una mesa en uso, tampoco pasada la medianoche mientras seguiría la noche que el día cerrado quitó. Fuera de horario la mesa sigue pidiendo, como en la Fase 19. Es una decisión por omisión que el dueño del producto debe confirmar (`PublicOrderRules.TableOverridesClosure`).
  - **WhatsApp dice el horario:** las preguntas de horario reciben el horario real y los días especiales de las próximas dos semanas. Con horario de la semana, la descripción del negocio ya no las contesta, salvo que también pregunten dónde están; también la debe confirmar el dueño del producto. Mientras el negocio está cerrado, el enlace del menú en modo pedidos se ofrece solo para verlo.
  - **Mensaje del pedido rechazado:** a 0 y 1 días es el mismo ("Ahora está cerrado: abre mañana a las 9:00."), salvo una apertura a medianoche, que dice solo el día ("abre mañana", antes "abre mañana a las 0:00"), y la hora de la una, en singular ("a la 1:00", antes "a las 1:00"); desde 2 días nombra la fecha ("abre el sábado 26 a las 9:00", "abre el sábado 2 de enero a las 9:00", "abre el martes 5 de enero a las 9:00").
  - **Prueba de humo:** también exige `"business-time-zones":"Healthy"` en `/health/ready`. Un servidor sin los datos de zonas horarias hace fallar ahora el despliegue (con el slot de staging, no llega a producción) en lugar de pasar con los horarios en UTC.
- **Volver a la versión anterior:** no necesita nada en la base. Esa versión no conoce la tabla: los días especiales se quedan guardados sin efecto (los menús reciben pedidos en las fechas cerradas y WhatsApp vuelve a contestar con la descripción) y vuelven a aplicarse al publicar otra vez esta versión. Un navegador con el cliente nuevo sobre la API anterior usa la redacción de antes, porque no recibe `todayDate`.
- **Dejar también la base como estaba:** aplica el `rollback.sql`. Borra la tabla y con ella los días especiales de todos los negocios, también los pasados (la bitácora conserva cada cambio con sus valores: `SpecialDayAdded`, `SpecialDayUpdated` y `SpecialDayRemoved`); antes, la consulta 4 de `verify.sql` cuenta las filas, los negocios y las fechas desde hoy que se perderían. Es preferible corregir hacia adelante.

## Pedidos de Rappi, Uber Eats y DiDi Food (Fase 22F)

La Fase 22F trae una migración, [`database/production/20261012005540_AddDeliveryPlatformOrders`](../../database/production/20261012005540_AddDeliveryPlatformOrders). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql`, el merge, configurar las plataformas y capturar pedidos. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0013](../adr/0013-delivery-platform-orders.md)).

- **La migración:**
  - **Qué agrega:** crea `DeliveryPlatformSettings` y `PlatformOrderDetails`, vacías, y agrega `DailySalesSnapshots.PlatformAmount` con 0 por omisión. `Orders`, `OrderPayments`, `OrderRefunds` y `CashTransactions` no cambian.
  - **Cuándo cambia algo:** cuando el dueño activa una plataforma en Configuración › Negocio › Plataformas de reparto.
  - **Después de aplicarla:** el `verify.sql` muestra las columnas, las once restricciones CHECK y los índices. Sus consultas 4 y 5 (pedidos con canal 3 a 5 y pagos o reembolsos con el método 5) siguen en 0 hasta desplegar.
- **Mientras se publica:** la versión de `master` (Fase 21) no conoce las tablas y sigue funcionando. Sus días guardados toman `PlatformAmount` 0 y su validación rechaza los canales 3 a 5. Se ensayó sobre la base ya aplicada: `/health/schema` sano, una venta de mostrador cobrada y su worker ([migraciones](database-migrations.md)). Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **No publiques entre las 00:00 y las 00:45, hora del negocio.** Si el swap cruza la medianoche, la instancia anterior puede guardar el día con lo que cobraron las plataformas en «Otros». La consulta 7 del `verify.sql` lo detecta y [`database/production/data-repair/20261012_PlatformAmountSnapshots`](../../database/production/data-repair/20261012_PlatformAmountSnapshots) lo repara con la versión nueva en todas las instancias.
- **El contrato solo agrega:**
  - **Rutas:** `GET /api/v1/delivery-platforms` y `PUT /api/v1/delivery-platforms/{channel}` (`[RequiresTenantManager]`), `GET /api/v1/delivery-platforms/channels` (`ManageOrders` o `ViewReports`), y `GET /api/v1/reports/sales-by-channel` y `GET /api/v1/reports/delivery-platform-orders` (`ViewReports`, con `search` opcional), con sus DTOs. `PUT /api/v1/orders/{id}/cancel` acepta `restockItems` opcional (`true` si no viene).
  - **Enums:** los valores 3 a 5 de `OrderChannel`, el 5 de `OrderPaymentMethod` («Plataforma»), y `PlatformCollectedBy` y `PlatformCommissionBase`.
  - **Campos:**
    - en la venta, `externalOrderNumber`, `collectedBy` y `confirmPossibleDuplicate`, opcionales;
    - `platformOrder` en `OrderDto` (el folio y quién cobró, sin dinero);
    - `channel` y `externalOrderNumber` en `KitchenOrderDto` y en el aviso en vivo `OrderCreated`;
    - `platformTotal`, `platformPaymentsCount` y `platformRefundsTotal` en el corte del día;
    - `platformCollected` en Inicio y `platformAmount` en las ventas por sucursal.
  - **Código de error:** `possible_duplicate_order` (409), compatible según el [ADR 0008](../adr/0008-api-error-contract.md).

  Nada se quita ni cambia de nombre.
- **Pestañas abiertas antes del despliegue:** el cliente anterior, hasta que se recarga (el aviso de versión nueva lo ofrece), ve un pedido de plataforma como uno de mostrador y el método «Plataforma» como «5». Su corte del día no tiene la fila de plataformas, así que «Cobrado» puede ser mayor que la suma de sus filas. Vender no ofrece plataformas en ese cliente. El servidor decide igual.
- **Cambios de comportamiento:**
  - **«Otros» del corte:** desde la Fase 22F es todo método sin cubeta propia. Con los datos de hoy da lo mismo.
  - **`NetCashFlow`:** deja fuera lo que cobraron las plataformas. Sin pedidos de plataforma no cambia.
  - **Avisos y campañas:** un pedido de plataforma nunca manda avisos de WhatsApp ni entra a las campañas.
- **Volver a la versión anterior:**
  - **Sin pedidos de plataforma:** funciona sin tocar la base, porque no conoce las tablas ni la columna. La configuración de las plataformas se queda guardada sin efecto.
  - **Con pedidos de plataforma:** no conviene. Esa versión sacaría de una caja la devolución de una anulación y su worker pondría en «Otros» lo que cobraron las plataformas. Se corrige hacia adelante.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50008, sin cambiar nada, si hay un pedido con canal 3 a 5 o un pago, reembolso o movimiento de caja con el método 5; su consulta 5 dice qué lo detiene. Sin ellos, borra las dos tablas y la columna. Se pierde la configuración de cada plataforma, que la bitácora conserva (`DeliveryPlatformUpdated`).

## Precios en las apps de las plataformas (Fase 22F-2)

La Fase 22F-2 trae una migración, [`database/production/20261012112823_AddPlatformPrices`](../../database/production/20261012112823_AddPlatformPrices). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql`, el merge, y poner los precios en Catálogo › Precios en apps. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0014](../adr/0014-delivery-platform-prices.md)).

- **La migración:**
  - **Qué agrega:** crea `ProductPlatformPrices` y `ProductOptionPlatformPrices`, vacías. No toca ninguna otra tabla.
  - **Cuándo cambia algo:** cuando alguien con `ManageCatalog` guarda precios en Catálogo › Precios en apps. Mientras no haya precios, los pedidos de plataforma se cobran con el precio del menú, como en la Fase 22F.
  - **Después de aplicarla:** el `verify.sql` muestra las columnas, las cuatro restricciones CHECK, las dos llaves en cascada y los seis índices. Su consulta 4 (los precios guardados) sigue vacía hasta desplegar, y la 5 (precios de otro negocio que su producto u opción) debe quedar vacía siempre.
- **Mientras se publica:** la versión de `master` (Fase 22F) no conoce las tablas y sigue funcionando: ninguna de sus consultas las lee, y al quitar una opción o borrar un producto la base se lleva sus precios (`PlatformPricesMigrationSqlServerTests`, ver [migraciones](database-migrations.md)). Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:**
  - **Rutas:** `GET` y `PUT /api/v1/delivery-platforms/{channel}/prices` (`ManageCatalog`; 403 sin él).
    - **Esquemas:** `PlatformPriceItemDto` (y su página, `PlatformPriceItemDtoPaginatedList`), `PlatformPriceOptionGroupDto`, `PlatformPriceOptionDto`, `SavePlatformPricesRequest`, `PlatformProductPriceRequest` y `PlatformOptionPriceRequest`.
    - **Campos obligatorios:** `products`, `price` y `priceDelta` son `[JsonRequired]` y el contrato los marca como requeridos: null se manda explícito (vuelve al precio del menú), y sin el campo, o con un elemento null en la lista, la API responde 400 `validation_failed`.
    - **Respuestas del `PUT`:** 204; 400 `validation_failed` (la petición) o `business_rule` (un producto ajeno, un servicio, un precio fuera de los límites o un negocio sin el módulo de productos ni el de alimentos); y 409 `concurrency_conflict` si otra persona guardó al mismo tiempo el primer precio del mismo producto u opción, o lo borró.
  - **Parámetro:** `GET /api/v1/products` acepta `priceChannel`, opcional: solo un canal de plataforma (400 si no).
  - **Permiso ampliado:** `GET /api/v1/delivery-platforms/channels` también responde a `ManageCatalog`, sin tasas.
  - **Mismo contrato, otros importes:** `POST /api/v1/orders` con canal 3 a 5 no cambia de forma, pero cobra los precios de la app donde los hay.

  Nada se quita ni cambia de nombre.
- **Pestañas abiertas antes del despliegue:** el cliente anterior no conoce Precios en apps, y su Vender cobra los pedidos de plataforma con el precio del menú. Si el negocio ya puso un precio en la app, la venta se rechaza porque el total no es el que calcula el servidor, hasta que la pantalla se recarga (el aviso de versión nueva lo ofrece). El servidor decide igual.
- **Volver a la versión anterior:** funciona sin tocar la base, porque no conoce las tablas. Los precios se quedan guardados sin efecto: los pedidos de plataforma vuelven a cobrarse con el precio del menú, en Vender y en la venta a la vez, y los precios vuelven a aplicarse al publicar otra vez esta versión. Una pestaña que ya tenía el cliente nuevo, hasta recargarse, sigue pidiendo `priceChannel` (la versión anterior lo ignora): su ticket dice «Con tus precios de Rappi» sobre los precios del menú, que son los que se cobran, y Precios en apps responde 404.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50009, sin cambiar nada, si hay algún precio guardado, porque ninguna bitácora los conserva; su consulta 4 dice cuántos son y de cuántos negocios. Sin precios, borra las dos tablas. Es preferible corregir hacia adelante.

## Liquidación de los depósitos de las plataformas (Fase 22G)

La Fase 22G trae una migración, [`database/production/20261012145908_AddPlatformSettlements`](../../database/production/20261012145908_AddPlatformSettlements). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0015](../adr/0015-delivery-platform-settlements.md)).

- **La migración:**
  - **Qué agrega:** crea `PlatformSettlements`, `PlatformSettlementOrders` y `PlatformSettlementAdjustments` (con el lugar de cada ajuste, para leerlos en su orden), vacías. No toca ninguna otra tabla.
  - **Cuándo cambia algo:** cuando un dueño o administrador registra un depósito en Reportes › Liquidaciones. Mientras no haya liquidaciones, todo sigue como en la Fase 22F-2.
  - **Después de aplicarla:** el `verify.sql` muestra las columnas, las nueve restricciones CHECK, las tres llaves (dos en cascada y la del pedido sin ella) y los índices, con el filtro de los dos únicos. Su consulta 4 (las liquidaciones guardadas) sigue vacía hasta desplegar, y las cinco de la 5 (consistencia) deben quedar vacías siempre.
- **Mientras se publica:** la versión de `master` (Fase 22F-2) no conoce las tablas y sigue funcionando: ninguna de sus consultas las lee, y un pedido liquidado se puede reembolsar porque un reembolso no borra el pedido (`PlatformSettlementsMigrationSqlServerTests`, ver [migraciones](database-migrations.md)). Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:**
  - **Rutas** (`api/v1/platform-settlements`):
    - `GET /`, con `channel`, `status`, `search`, `pageNumber` y `pageSize` (20 por omisión, hasta 100);
    - `GET /receivable`;
    - `GET /pending-orders?channel=&from=&to=`, con el periodo obligatorio: de hasta 31 días, desde 2000 y hasta hoy;
    - `GET /{id}`;
    - `POST /` responde 201, y `PUT /{id}`, `POST /{id}/close` y `POST /{id}/void` responden 200.
    - Leer pide `ViewReports`, y cambiar, dueño o administrador; 403 sin ellos.
  - **Esquemas:** `PlatformSettlementDto` (y su página), `PlatformSettlementDetailDto`, `PlatformSettlementOrderDto`, `PlatformSettlementAdjustmentDto`, `PendingPlatformOrdersDto`, `PlatformReceivableDto`, `CreatePlatformSettlementRequest`, `UpdatePlatformSettlementRequest`, `PlatformSettlementAdjustmentRequest`, `ClosePlatformSettlementRequest` y `VoidPlatformSettlementRequest`. Los enums `PlatformSettlementStatus` y `PlatformSettlementAdjustmentKind` viajan como números.
  - **Campos obligatorios:** todo menos la referencia, las notas y la descripción de un ajuste es `[JsonRequired]`: sin el campo, o con un ajuste null, la API responde 400 `validation_failed`.
  - **Respuestas de los cambios:** 400 `validation_failed` o `business_rule`; 404 si la liquidación es de otro negocio; 409 `concurrency_conflict` con una revisión vieja, una carrera con otra persona o el mismo guardado enviado dos veces; y 409 `conflict`, con su motivo, cuando otra liquidación ya tenía un pedido o cuando, al cerrar, cambió lo que se espera de uno (las cifras nuevas quedan guardadas).

  Nada se quita ni cambia de nombre.
- **Pestañas abiertas antes del despliegue:** el cliente anterior no conoce Reportes › Liquidaciones ni la pestaña «Ventas»: las ve al recargar (el aviso de versión nueva lo ofrece). Nada de lo que hacía cambia.
- **Volver a la versión anterior:** funciona sin tocar la base, porque no conoce las tablas. Las liquidaciones se quedan guardadas sin efecto, y Reportes › Liquidaciones vuelve al publicar otra vez esta versión. Una pestaña con el cliente nuevo recibe 404 en Liquidaciones hasta recargarse.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50010, sin cambiar nada, si hay alguna liquidación guardada, porque la bitácora no guarda qué pedidos lleva cada una (solo cuántos) ni nada de una abierta; la consulta 4 del `verify.sql` dice cuántas son, de qué plataforma y estado, y de cuántos negocios. Sin liquidaciones, borra las tres tablas. Es preferible corregir hacia adelante.

## Sucursal solo a domicilio (Fase 22A)

La Fase 22A trae una migración, [`database/production/20261012204959_AddBranchServiceMode`](../../database/production/20261012204959_AddBranchServiceMode). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0016](../adr/0016-delivery-only-branches.md)).

- **La migración:**
  - **Qué agrega:** a `Branches`, la columna `ServiceMode` (`int NOT NULL DEFAULT 0`) y la restricción `CK_Branches_ServiceMode`. Todas las sucursales que ya existen quedan como locales. No toca ninguna otra tabla.
  - **Cuándo cambia algo:** cuando un dueño o administrador marca una sucursal «Solo a domicilio» en Configuración › Sucursales, o un negocio nuevo se registra con el giro «Comida a domicilio». Con la principal así, su dirección deja de salir en el menú en línea.
  - **Después de aplicarla:** el `verify.sql` muestra la columna, su `DEFAULT` y su restricción. Su consulta 3 cuenta las sucursales por modo y lista las solo a domicilio (todas en 0 y ninguna hasta desplegar), y la 4 (consistencia) debe quedar vacía siempre.
- **Mientras se publica:** la versión de `master` (Fase 22G) no conoce la columna y sigue funcionando: sus sucursales nuevas toman el 0 del `DEFAULT`, y editar una no la cambia (`BranchServiceModeMigrationSqlServerTests`). Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:** `BranchDto.serviceMode`; `serviceMode` opcional en `CreateBranchCommand` y `UpdateBranchCommand` (sin él, una nueva tiene local y una guardada conserva el que tenía; un valor desconocido es 400 `validation_failed`); `PublicMenuDto.deliveryOnly`; y el enum `BranchServiceMode`, que viaja como número. `PublicMenuDto.address` ya podía ser nulo. Nada se quita ni cambia de nombre.
- **Pestañas abiertas antes del despliegue:** el cliente anterior no conoce el campo. Guardar una sucursal desde él no lo manda, así que la sucursal conserva cómo atiende: nunca convierte una cocina en local con la dirección a la vista. En el menú en línea de una cocina sin local, el cliente anterior ya no recibe la dirección, pero tampoco muestra «Solo a domicilio» hasta recargarse.
- **Volver a la versión anterior:** funciona sin tocar la base, pero esa versión trata todas las sucursales como locales: **vuelve a publicar la dirección de una principal solo a domicilio** en el menú en línea. Antes de volver, la consulta 3 del `verify.sql` dice cuáles hay; cámbiala a local y borra su calle, o acepta que se publique.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50011, sin cambiar nada, mientras haya una sucursal solo a domicilio, por la misma razón. Sin ellas, borra la restricción y la columna, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Zonas de entrega (Fase 22B)

La Fase 22B trae una migración, [`database/production/20261013002425_AddDeliveryZones`](../../database/production/20261013002425_AddDeliveryZones). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0017](../adr/0017-delivery-zones.md)).

- **La migración:**
  - **Qué agrega:** dos tablas nuevas y vacías, `DeliveryZones` y `DeliveryZonePostalCodes`, con sus restricciones, la llave en cascada y los índices únicos de los nombres y de los códigos postales del negocio. No toca otras tablas.
  - **Cuándo cambia algo:** cuando un dueño o administrador agrega una zona en Configuración › Zonas de entrega. Desde ese momento, las respuestas automáticas contestan si llegan a un código postal y cuánto cuesta el envío.
  - **Después de aplicarla:** el `verify.sql` muestra las columnas, las restricciones, la llave y los índices. Su consulta 4 cuenta las zonas (ninguna hasta desplegar) y la 5 (consistencia) debe quedar vacía siempre.
- **Mientras se publica:** la versión de `master` (Fase 22A) no conoce las tablas y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:** `GET`, `POST`, `PUT /{id}` y `DELETE /{id}?revision=` en `/api/v1/delivery-zones`, con `DeliveryZoneDto` y `SaveDeliveryZoneRequest`. Nada se quita ni cambia de nombre.
- **Volver a la versión anterior:** funciona sin tocar la base: no conoce las tablas, y las zonas se quedan sin usarse. Sus respuestas automáticas vuelven a no saber de zonas.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50012, sin cambiar nada, mientras haya zonas guardadas: se perderían, y la bitácora no guarda sus códigos postales. Sin ellas, borra las dos tablas, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Pedido a domicilio (Fase 22C)

La Fase 22C trae una migración, [`database/production/20261013042014_AddOrderDeliveries`](../../database/production/20261013042014_AddOrderDeliveries). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0018](../adr/0018-home-delivery-orders.md)).

- **La migración:**
  - **Qué agrega:** una tabla nueva y vacía, `OrderDeliveries`, con sus restricciones, la llave en cascada a `Orders` y el índice único del pedido. No toca otras tablas.
  - **Cuándo cambia algo:** cuando un negocio con zonas de entrega activas recibe un pedido a domicilio, desde el menú en línea o Vender. Sin zonas, todo sigue como antes.
  - **Después de aplicarla:** el `verify.sql` muestra las columnas, las restricciones, la llave y el índice. Su consulta 4 cuenta los pedidos a domicilio por estado (ninguno hasta desplegar) y la 5 (consistencia) debe quedar vacía siempre.
  - **Antes del merge, la consulta 6:** lista los negocios con zonas activas cuyos precios no incluyen IVA, con su tasa y el envío menor y mayor de sus zonas. A esos negocios el pedido les suma el IVA al envío, pero la pantalla de la 22B les decía que los montos eran con IVA incluido: desde ahora Configuración › Zonas de entrega lo explica, y las respuestas automáticas, el menú en línea y Vender dicen «más IVA». Avísales, para que corrijan sus envíos si los escribieron con IVA incluido. Vacía, no hay a quién avisar.
- **Mientras se publica:** la versión de `master` (Fase 22B) no conoce la tabla y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:** `PUT /api/v1/orders/{id}/delivery/shipment` y `GET /api/v1/public/menu/{slug}/delivery-quote`; `delivery` en `CreateOrderCommand`, `PlacePublicOrderRequest` y `OrderDto`; `isDelivery` en `KitchenOrderDto`; `deliveryAvailable` en `PublicMenuDto`; `deliveryFee` en `PublicOrderReceiptDto`; `isDeliveryFee` en `CfdiConceptDto`, y el enum `DeliveryCourier`. Nada se quita ni cambia de nombre. Un cliente abierto antes del despliegue sigue vendiendo como antes y ve el envío dentro del total hasta recargarse.
- **Volver a la versión anterior:** funciona sin tocar la base: no conoce la tabla y cobra cada pedido a domicilio por su total guardado, envío incluido. Pero no muestra la dirección ni el envío, ni en Pedidos ni en el ticket, y deja de ofrecer domicilio. Antes de volver, la consulta 4 del `verify.sql` dice cuántos pedidos a domicilio hay abiertos (estados 0 a 3).
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50013, sin cambiar nada, mientras haya pedidos a domicilio: se perderían su dirección y su envío. Sin ellos, borra la tabla, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Salida a entregar y liquidación del repartidor (Fase 22D)

La Fase 22D trae una migración, [`database/production/20261013085926_AddCourierSettlements`](../../database/production/20261013085926_AddCourierSettlements). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0019](../adr/0019-courier-cash-on-delivery.md)).

- **La migración:**
  - **Qué agrega:** cuatro columnas que aceptan nulos en `OrderDeliveries` (`PaysWith`, `DispatchedAt`, `DispatchedBy`, `CashToCollect`) con sus restricciones, y tres tablas nuevas y vacías: `CourierSettlements`, `CourierSettlementOrders` y `CourierSettlementAdjustments`. No cambia datos.
  - **Cuándo cambia algo:** cuando un pedido a domicilio sale con el repartidor, o un cliente dice con cuánto paga. Sin pedidos a domicilio, todo sigue como antes.
  - **Después de aplicarla:** el `verify.sql` muestra las columnas, las restricciones, las llaves y los índices. Su consulta 4 cuenta los pedidos que salieron o tienen «paga con» por estado, lo que cobra y lo que debe el repartidor, y las liquidaciones por negocio (nada hasta desplegar); la 5 (consistencia) debe quedar vacía siempre.
- **Mientras se publica:** la versión de `master` (Fase 22C) no conoce las columnas ni las tablas y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El aviso «En camino»** usa la plantilla `order_status_update`, que ya está aprobada en Meta: no hay plantilla nueva que dar de alta.
- **El contrato solo agrega:** `PUT /api/v1/orders/{id}/delivery/dispatch`; `GET /api/v1/courier-settlements`, `/receivable`, `/pending-orders` y `/{id}`, y `POST /api/v1/courier-settlements`; `paysWith` en la entrega de `CreateOrderCommand` y `PlacePublicOrderRequest`; `paysWith`, `dispatchedAt`, `dispatchedBy`, `cashToCollect` y `awaitsCourierCash` en la entrega de `OrderDto`; el enum `CourierSettlementAdjustmentKind`, y el evento `OrderUpdated` del hub de pedidos. Nada se quita ni cambia de nombre. Un cliente abierto antes del despliegue sigue vendiendo como antes hasta recargarse.
- **Volver a la versión anterior:** funciona sin tocar la base: no conoce las columnas ni las tablas, y los pagos de una liquidación son pagos normales de cada pedido. Pero no muestra la salida, lo que debe el repartidor ni sus liquidaciones. Antes de volver, la consulta 4 del `verify.sql` dice cuánto debe el repartidor.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50014, sin cambiar nada, mientras haya pedidos que salieron o con «paga con», o liquidaciones: se perdería lo que cobra el repartidor y lo que entregó. Sin ellos, quita lo que agregó, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Servicios a domicilio (Fase 22E)

La Fase 22E trae una migración, [`database/production/20261013191311_AddHomeVisits`](../../database/production/20261013191311_AddHomeVisits). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0020](../adr/0020-home-visits.md)).

- **La migración:**
  - **Qué agrega:** `HomeVisitTravelMinutes` en `TenantSettings` (30 en cada negocio), `SaleOrderId` (con nulos) y la versión (`rowversion`) en `Appointments`, y dos tablas nuevas y vacías: `AppointmentVisits` y `OrderVisitCharges`, con sus restricciones, llaves e índices. No cambia otros datos.
  - **Cuándo cambia algo:** cuando un negocio con Agenda y zonas de entrega agenda una cita a domicilio, o cobra una cita desde la Agenda. Sin eso, todo sigue como antes, salvo que la agenda deja 30 minutos de traslado solo alrededor de las visitas (no hay ninguna todavía).
  - **Después de aplicarla:** el `verify.sql` muestra las columnas, las restricciones, las llaves y los índices. Su consulta 4 cuenta por negocio las visitas, las abiertas, los cargos por visita cobrados y su suma, y las citas cobradas (nada hasta desplegar); la 5 (consistencia) debe quedar vacía siempre.
- **Mientras se publica:** la versión de `master` (Fase 22D) no conoce las columnas ni las tablas y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **Los avisos de cita** siguen sin plantilla de Meta (el sistema los escribe): no hay plantilla nueva que dar de alta.
- **El contrato solo agrega:** `GET /api/v1/appointments/route` y `/visit-quote`; `GET` y `PUT /api/v1/delivery-zones/home-visits`; el filtro `homeVisit` de `GET /api/v1/appointments`; `visit` en la cita que se crea o edita y en `AppointmentDto`, con `saleOrderId` y `saleCancelled` (también en cada parada de la ruta); `appointmentId` en `CreateOrderCommand`; `visitCharge` en `OrderDto`, e `isVisitCharge` en `CfdiConceptDto`. Nada se quita ni cambia de nombre.
  - Editar una cita sin `visit` la deja en el negocio: un cliente abierto antes del despliegue que edita una cita a domicilio le quitaría la visita. El aviso de versión nueva (Fase 20A) pide recargar; mientras no haya visitas no pasa nada.
  - Dos ediciones de la misma cita al mismo tiempo ahora dan 409 a la segunda, en lugar de quedarse con la última.
  - Una venta con `appointmentId` pide también `ManageAppointments`.
- **Volver a la versión anterior:** funciona sin tocar la base: no conoce las columnas ni las tablas, sigue agendando y cada venta conserva su total guardado, cargo incluido. Pero no muestra las visitas ni el cargo, no deja tiempo de traslado y una cita cobrada se podría cobrar otra vez. Antes de volver, la consulta 4 del `verify.sql` dice cuántas visitas hay abiertas.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50015, sin cambiar nada, mientras haya visitas, cargos por visita o citas cobradas: se perderían las direcciones, los cargos y qué venta cobró cada cita. Sin ellos, quita lo que agregó, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Registro confiable (Fase 23A)

La Fase 23A no tiene migración ni `apply.sql`: la cola (`OutboxMessages`) ya existe.

- **El proveedor de correo:** sin él, nadie que se registre puede activar su cuenta. Mientras falte, `/health/ready` queda Degraded (sigue respondiendo 200) y la prueba de humo pasa con un aviso (arriba).
- **Los correos de las cuentas salen de la cola** (`AccountEmailRequestedHandler`), después de responder: la activación, la recuperación de contraseña, el aviso a quien se registra con un correo que ya tiene cuenta y las invitaciones al equipo. Necesitan los workers: con `BackgroundJobs__Enabled=false` (por ejemplo el slot de staging) se guardan y salen cuando una instancia con workers los atiende, si no pasó un día. Un rechazo pasajero del proveedor se reintenta como cualquier mensaje de la cola; después de 8 intentos queda apartado y `background-queues` lo dice.
- **Mientras se publica:** la versión anterior manda sus correos dentro de la petición, como siempre. Si en los segundos en que corren las dos su worker toma un correo que guardó la versión nueva, no conoce el evento y lo aparta (`Unknown event type` en el log): quien no recibió su enlace lo pide otra vez, y `background-queues` queda Degraded durante un día.
- **El contrato no cambia de forma**, pero dos respuestas sí:
  - registrarse con un correo que ya tiene cuenta responde como un registro nuevo («Revisa tu correo»), en lugar de «Ya existe una cuenta con este correo electrónico.»;
  - iniciar sesión con una cuenta sin activar responde como una contraseña incorrecta, con un texto que menciona la activación, en lugar de «Activa tu cuenta…».
- **Volver a la versión anterior:** funciona sin tocar la base, pero aparta del mismo modo los correos de cuenta que sigan en la cola, y vuelve a decir si un correo tiene cuenta.

## Explorar, el directorio de negocios (Fase 24A)

La Fase 24A trae una migración, [`database/production/20261016162347_AddBusinessDirectory`](../../database/production/20261016162347_AddBusinessDirectory). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0021](../adr/0021-business-directory.md)).

- **La migración:**
  - **Qué agrega:** `ListedInDirectory` en `TenantSettings` (encendido en cada negocio), y `DirectoryCategory` y `PublicDescription` (con nulos) en `Tenants`, con dos CHECK. Da a cada negocio la categoría de su giro, o la de su tipo si el giro no es de la lista del registro. No cambia otros datos.
  - **Antes de aplicarla:** la consulta 4 del `verify.sql` muestra la categoría que recibirá cada giro y cuántos negocios pueden aparecer en Explorar: activos y con el menú en línea publicado. Aparecen los que además tienen algún producto que su menú muestra, que la consulta no cuenta. Los giros escritos a mano toman la categoría de su tipo, y el dueño la puede cambiar.
  - **Después de aplicarla:** la misma consulta muestra la categoría guardada. La 5 (decisiones de los dueños) queda vacía hasta que alguno cambie algo, y la 6 (descripciones con saltos de línea) debe quedar vacía siempre.
- **Desde el despliegue, `/` lleva a Explorar a visitantes y clientes**, y Explorar lista los negocios activos con el menú publicado y algún producto que su menú muestra. Un negocio que no quiera aparecer apaga «Aparecer en Explorar» en Configuración › Negocio; su enlace y su QR siguen funcionando. El equipo de un negocio sigue entrando a su trabajo.
- **Mientras se publica:** la versión de `master` (Fase 23A) no conoce las columnas, y los negocios que abra toman los DEFAULT (Otros, en Explorar). Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:** `GET /api/v1/public/directory` (anónimo, con el límite `public-menu`), `GET` y `PUT /api/v1/auth/my-tenant/directory`, y los enums `BusinessCategory` y `DirectoryVisibility`. Nada se quita ni cambia de nombre.
- **Una foto por instancia:** cada instancia rehace la lista de Explorar como mucho cada 30 segundos, sin configuración ni servicio externo. Con varias instancias, dos peticiones pueden ver estados distintos durante ese tiempo.
- **Volver a la versión anterior:** funciona sin tocar la base, pero no hay Explorar: `/` vuelve a pedir iniciar sesión, y lo que decidieron los dueños queda guardado sin usarse.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50016, sin cambiar nada, en cuanto un dueño decidió cómo se ve su negocio: lo ocultó, escribió su descripción o cambió su categoría (`DirectoryProfileUpdated` en la bitácora). Volver a aplicar perdería esa decisión y listaría otra vez al que se ocultó. Si nadie decidió nada, quita lo que agregó, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Fotos del servicio (Fase 25)

La Fase 25 trae una migración, [`database/production/20261017183622_AddServiceOrderPhotos`](../../database/production/20261017183622_AddServiceOrderPhotos). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0024](../adr/0024-service-photos.md)).

- **La migración:**
  - **Qué agrega:** la tabla `ServiceOrderPhotos`, nueva y vacía: una fila por foto agregada a una orden de servicio, con la CHECK de su etapa (`CK_ServiceOrderPhotos_Stage`), la llave a `Orders` con borrado en cascada y dos índices. No toca `Orders` ni las fotos que ya guardan las órdenes al recibirse.
  - **Cuándo cambia algo:** cuando alguien agrega una foto desde Pedidos o desde el tablero. Sin eso, todo sigue como antes.
  - **Después de aplicarla:** el `verify.sql` muestra la tabla con sus 10 columnas, la CHECK, la llave y los índices. Su consulta 3 cuenta las fotos por etapa, órdenes y negocios (nada hasta desplegar); la 4 (consistencia) debe quedar vacía siempre.
- **Mientras se publica:** la versión de `master` (Fase 24C) no conoce la tabla y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:** `GET` y `POST /api/v1/orders/{id}/service-photos`, `DELETE /api/v1/orders/{id}/service-photos/{photoId}` y el enum `ServicePhotoStage`. `POST /api/v1/orders/uploads/service-photo` acepta también `ManageServiceBoard` y ahora pide el módulo de servicios. Nada se quita ni cambia de nombre.
- **Volver a la versión anterior:** funciona sin tocar la base: no conoce la tabla, y las fotos agregadas quedan guardadas sin mostrarse hasta volver a la nueva. Pedidos y el tablero vuelven a mostrar solo las fotos de la recepción.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50018, sin cambiar nada, en cuanto un negocio agregó una foto: se perdería la evidencia de lo que recibió y entregó. Sin fotos, quita la tabla, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Citas en línea (Fase 24D)

La Fase 24D-1 trae una migración, [`database/production/20261017225015_AddOnlineBooking`](../../database/production/20261017225015_AddOnlineBooking). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración anterior, también la aplica ([ADR 0025](../adr/0025-online-booking.md)).

- **La migración:**
  - **Qué agrega:**
    - a `TenantSettings`, `OnlineBookingEnabled` (apagadas, `0`) y `OnlineBookingCapacity` (`1`), con la CHECK de 1 a 20 (`CK_TenantSettings_OnlineBookingCapacity`);
    - a `Appointments`, `Channel` (`0`, el negocio), `CustomerUserId` (con llave a `AspNetUsers`, sin borrado en cascada), `ClientRequestId`, `TrackingToken` (`varchar(43)`) y `CancelledByCustomerAt`, todas vacías en las citas que ya existen; las CHECK `CK_Appointments_Channel` y `CK_Appointments_TrackingToken` (43 caracteres); y tres índices filtrados: las citas de una cuenta, la llave de la solicitud única por negocio y el enlace único.
  - **Cuándo cambia algo:** cuando un dueño prende las citas en línea en Configuración y un cliente agenda. Sin eso, la agenda sigue igual: las citas guardadas quedan como del negocio.
  - **Después de aplicarla:** el `verify.sql` muestra las 7 columnas, las 3 CHECK, la llave y los 3 índices. Su consulta 3 cuenta los negocios que las prendieron y las citas en línea por estado (nada hasta desplegar); la 4, las solicitudes por confirmar; la 5 (consistencia) debe quedar vacía siempre.
- **Mientras se publica:** la versión de `master` (Fase 25) no conoce las columnas y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:**
  - públicas: `GET /api/v1/public/menu/{slug}/booking`, `GET /api/v1/public/menu/{slug}/booking/slots`, `POST /api/v1/public/menu/{slug}/appointments`, `POST /api/v1/public/appointments/tracking` y `POST /api/v1/public/appointments/tracking/cancel`;
  - de la cuenta: `GET /api/v1/me/appointments` y `POST /api/v1/me/appointments/{id}/cancel`;
  - del negocio: `POST /api/v1/appointments/{id}/accept`, `POST /api/v1/appointments/{id}/decline` y `GET`/`PUT /api/v1/appointments/online-booking`;
  - los enums `AppointmentChannel` y `MyAppointmentsScope`, el valor `Requested` (5) de `AppointmentStatus`, `channel` y `cancelledByCustomer` en las citas, `booksOnline` en el menú y en Explorar, y la clave `appointmentRequests` de los avisos. Nada se quita ni cambia de nombre.
- **Volver a la versión anterior:** funciona sin tocar la base, pero no conoce «Por confirmar»: mostraría las solicitudes sin poder aceptarlas ni rechazarlas. La consulta 4 del `verify.sql` dice cuántas esperan; decídelas antes de volver. El menú y Explorar dejan de ofrecer «Agenda tu cita», y los enlaces de las citas dejan de abrir.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50019, sin cambiar nada, en cuanto un cliente agendó una cita en línea: se perdería de dónde vino, su cuenta y su enlace. Sin citas en línea, quita lo que agregó (también lo que eligieron los dueños, que no tiene efecto sin la versión nueva), y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Portada del menú (Fase 24E)

La Fase 24E trae una migración, [`database/production/20261018032200_AddBusinessCover`](../../database/production/20261018032200_AddBusinessCover). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración reciente, también la aplica; no sirve para crear una base vacía ([ADR 0026](../adr/0026-store-like-delivery-apps.md)).

- **La migración:** agrega a `Tenants` la columna `CoverImageUrl` (`nvarchar(500)` con nulos), vacía en todos los negocios. Cambia algo cuando un dueño sube su portada en Configuración: el menú la muestra arriba, y sin ella sigue con los colores del negocio. Después de aplicarla, el `verify.sql` muestra la columna, y su consulta 3 cuenta los negocios con y sin portada (todos sin portada hasta desplegar).
- **Los archivos:** la portada se guarda en el mismo almacenamiento que el logo y las fotos de los productos, en la carpeta pública `covers/{tenantId}`, en las mismas cinco variantes. No hay que configurar nada nuevo.
- **Mientras se publica:** la versión de `master` (Fase 24D-1) no conoce la columna y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:** `POST`/`DELETE /api/v1/auth/my-tenant/cover`, `coverImages` en el perfil del negocio, y `coverImageUrl`, `coverImageWideUrl`, `description` y `category` en el menú público, con `imageLargeUrl` en cada producto. Nada se quita ni cambia de nombre.
- **El menú cambia de forma** (portada, pestañas, filas con foto, la hoja del producto, el botón flotante y el pedido como página de pago), pero el pedido que llega al negocio es el mismo. Lo único nuevo es la nota de cada producto, que la cocina y Pedidos ya mostraban.
- **Volver a la versión anterior:** funciona sin tocar la base: no conoce la portada y vuelve el menú de antes.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50020, sin cambiar nada, en cuanto un dueño subió una portada: se perdería y sus archivos quedarían sin nada que los use. Sin portadas, quita la columna, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Buscadores y vistas previas (Fase 24D-2)

La Fase 24D-2 no trae migración ni configuración obligatoria ([ADR 0027](../adr/0027-search-engines-and-link-previews.md)):

- **Qué cambia al desplegar:**
  - Las páginas de las tiendas (`/m/{slug}`, `/m/{slug}/agendar`) llegan con su título, descripción, imagen y dirección en el `index.html`; las de Explorar, con su título, descripción y dirección.
  - Estas páginas tienen su propio límite por IP (`public-page`, 120 por minuto); una tienda que lo pasa abre la app igual con 429.
  - La API responde `/robots.txt` y `/sitemap.xml`.
  - Las demás pantallas de la app llevan `X-Robots-Tag: noindex`.
- **`AppUrl`:** la dirección canónica, las vistas previas y el sitemap usan `AppUrl` (la de los correos), por ejemplo `https://app.tienditapp.mx`. Sin ella, usan la dirección con que llegó cada petición, con https. Conviene configurarla en el App Service, sobre todo con un dominio propio, para que los buscadores no reciban la de `azurewebsites.net`.
- **Comprobarlo:**
  - `curl https://<app>/robots.txt` debe nombrar `Sitemap: <AppUrl>/sitemap.xml`.
  - El código fuente de una tienda publicada (`view-source:`) debe traer su `<title>`, `og:title`, `og:image` (si tiene portada o logo) y el bloque `application/ld+json`.
  - La herramienta de vista previa de enlaces de Facebook o la prueba de resultados enriquecidos de Google leen lo mismo.
- **Google Search Console (opcional):** verificar el dominio y enviar `/sitemap.xml` acelera que Google lea las tiendas. No hace falta para que funcione.
- **Volver a la versión anterior:** no toca la base. Regresa el `index.html` sin encabezado, y `/robots.txt` y `/sitemap.xml` responden 404.

## Productos y favoritos en Explorar (Fase 24F)

La Fase 24F trae una migración, [`database/production/20261018081803_AddFavoriteBusinesses`](../../database/production/20261018081803_AddFavoriteBusinesses). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración reciente, también la aplica; no sirve para crear una base vacía ([ADR 0028](../adr/0028-product-search-and-favorites.md)).

- **La migración:** crea la tabla `FavoriteBusinesses` (la cuenta, el negocio y cuándo lo guardó), con un índice único por cuenta y negocio. Su llave hacia `AspNetUsers` borra en cascada; la de `Tenants` no deja borrar un negocio con favoritos. No toca ninguna tabla existente. Después de aplicarla, el `verify.sql` muestra la tabla con sus índices, y su consulta 3 cuenta los favoritos (ninguno hasta desplegar).
- **Mientras se publica:** la versión de `master` (Fase 24D-2) no conoce la tabla y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:** `GET /api/v1/me/favorites`, `PUT` y `DELETE /api/v1/me/favorites/{slug}`, `products` en la respuesta de `GET /api/v1/public/directory` y `coverImageUrl` en cada negocio de Explorar. Nada se quita ni cambia de nombre.
- **La búsqueda de productos** no necesita configuración: arma su índice en la memoria de cada instancia con la foto de Explorar, la primera vez que alguien busca.
- **Volver a la versión anterior:** funciona sin tocar la base: no conoce la tabla, y Explorar vuelve a buscar solo negocios, sin portada ni favoritos.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50021, sin cambiar nada, en cuanto una cuenta guardó un favorito: borrar la tabla los perdería. Sin favoritos, quita la tabla, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## «Cerca de mí» (Fase 24G)

La Fase 24G trae una migración, [`database/production/20261018114538_AddBranchLocation`](../../database/production/20261018114538_AddBranchLocation). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración reciente, también la aplica; no sirve para crear una base vacía ([ADR 0029](../adr/0029-near-me.md)).

- **La migración:** agrega a `Branches` las columnas `Latitude` y `Longitude` (`decimal(9,6)` con nulos) y la restricción `CK_Branches_Location` (las dos o ninguna, en rango y nunca 0, 0). Todas las sucursales quedan fuera del mapa hasta que su dueño las ponga. Después de aplicarla, el `verify.sql` muestra las columnas y la restricción, y su consulta 3 cuenta las sucursales en el mapa (ninguna hasta desplegar).
- **Mientras se publica:** la versión de `master` (Fase 24F) no conoce las columnas, guarda las sucursales como siempre (quedan en NULL, que la restricción acepta) y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:**
  - `location` en el cuerpo de crear y editar una sucursal, y `latitude` y `longitude` en cada sucursal;
  - `latitude` y `longitude` en `GET /api/v1/public/directory`, y `distanceKm` en cada negocio y producto de Explorar;
  - `mapPoint` en el menú público.

  Nada se quita ni cambia de nombre.
- **Sin configuración ni servicios externos:** no hay mapa dentro de la app ni geocodificación; el dueño pega el punto de Google Maps o lo toma de su dispositivo.
- **Registros HTTP de App Service:** si se encienden, guardan la URL completa, con la ubicación de quien usa «Cerca de mí» (a unos 110 m). Es preferible dejarlos apagados.
- **Volver a la versión anterior:** funciona sin tocar la base: no conoce las columnas, y Explorar deja de ordenar por distancia.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50022, sin cambiar nada, en cuanto una sucursal está en el mapa: quitar las columnas olvidaría dónde está. Sin ninguna, quita la restricción y las columnas, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Reseñas de los negocios (Fase 24H)

La Fase 24H trae una migración, [`database/production/20261018141704_AddBusinessReviews`](../../database/production/20261018141704_AddBusinessReviews). Su `apply.sql` se aplica, como siempre, **antes del merge**: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y el merge. Es idempotente y, si a la base le falta una migración reciente, también la aplica; no sirve para crear una base vacía ([ADR 0030](../adr/0030-business-reviews.md)).

- **La migración:** crea la tabla `BusinessReviews` (la cuenta, el negocio, las estrellas, el comentario, la respuesta, el reporte y la moderación) con un índice único por cuenta y negocio, el de la lista de cada negocio y uno filtrado para los reportes. Sus restricciones (`CK_BusinessReviews_Rating`, `_Status`, `_Report`, `_Hidden` y `_Reply`) guardan las reglas del dominio. Su llave hacia `AspNetUsers` borra en cascada; la de `Tenants` no deja borrar un negocio con reseñas. No toca ninguna tabla existente. Después de aplicarla, el `verify.sql` muestra la tabla con sus índices y restricciones, y su consulta 3 cuenta las reseñas, las ocultas, las reportadas y las respondidas (ninguna hasta desplegar).
- **Mientras se publica:** la versión de `master` (Fase 24G) no conoce la tabla y sigue funcionando. Sin el `apply.sql`, la versión nueva responde 503 en `/health/schema` y la prueba de humo falla pidiéndolo.
- **El contrato solo agrega:**
  - `GET /api/v1/public/menu/{slug}/reviews` (anónimo, con el límite de peticiones del menú público) y `rating` en el menú público y en cada negocio de Explorar;
  - `GET /api/v1/me/reviews`, `GET`, `PUT` y `DELETE /api/v1/me/reviews/{slug}`;
  - `GET /api/v1/reviews`, `PUT` y `DELETE /api/v1/reviews/{id}/reply`, `POST /api/v1/reviews/{id}/report`;
  - `GET /api/v1/platform/reviews` y `POST /api/v1/platform/reviews/{id}/hide`, `keep` y `restore`.

  Nada se quita ni cambia de nombre.
- **Sin configuración ni servicios externos.**
- **Volver a la versión anterior:** funciona sin tocar la base: no conoce la tabla, y la tienda, Explorar y las cuentas responden como antes.
- **Dejar también la base como estaba:** el `rollback.sql` se detiene con el error 50023, sin cambiar nada, en cuanto un cliente reseñó un negocio: borrar la tabla perdería lo que escribieron. Sin reseñas, quita la tabla, y ejecutarlo otra vez no hace nada. Es preferible corregir hacia adelante.

## Slot de staging con swap y aprobación

Opcional, y el workflow ya lo soporta: mientras no existan sus variables, todo sigue como arriba. Con él, cada versión se despliega a un slot de staging y se prueba ahí (listo, esquema al día, cliente nuevo) mientras producción sigue con la anterior; el job `promote` espera la aprobación del environment `production`, hace el swap y prueba producción. Una versión que falla en el slot, por ejemplo porque no se aplicó su `apply.sql`, nunca llega a producción, y la anterior queda en el slot para volver a ella.

Requiere OIDC (sección anterior), porque el swap inicia sesión en Azure, y un plan Standard o superior.

1. **Slot.** En el App Service: *Deployment slots* → *Add slot*, nombre `staging`, clonando la configuración de producción. Comparte la base de datos de producción: el `apply.sql` se aplica antes del merge, como siempre.
2. **Ajustes del slot.** En el slot, *Configuration*: agrega `BackgroundJobs__Enabled` = `false` marcado como *Deployment slot setting*, para que el slot no procese colas ni envíe mensajes mientras prueba; al ser del slot, no viaja con el swap y producción conserva sus workers.
3. **Credencial federada del environment.** En la identidad de despliegue, agrega otra credencial federada con entidad *Environment* y nombre `production`. El job `promote` corre en ese environment y su sujeto es `repo:Eduard0corona/TienditApp:environment:production`; la credencial de la rama `master` sigue sirviendo al job que despliega al slot. El rol *Website Contributor* sobre la app ya cubre sus slots y el swap.
4. **Aprobación.** En GitHub: *Settings* → *Environments* → `production` (GitHub lo crea en la primera ejecución si no existe) → *Required reviewers* con quien aprueba, y en *Deployment branches* solo `master`.
5. **Variables.** En *Settings* → *Secrets and variables* → *Actions* → *Variables*: `AZURE_WEBAPP_SLOT` = `staging` y `AZURE_RESOURCE_GROUP` con el grupo de recursos de la app. Sin OIDC o sin el grupo, el workflow se detiene al empezar con un error que lo dice.
6. **Probar.** Corre el workflow desde `master`: el job de despliegue prueba `https://<app>-staging.azurewebsites.net`; el job `promote` pide la aprobación, hace el swap y prueba producción (o `SMOKE_TEST_URL`).

Para volver a la versión anterior después de un swap: *Deployment slots* → *Swap* (o `az webapp deployment slot swap --resource-group <grupo> --name <app> --slot staging --target-slot production`), que intercambia otra vez. Para dejar el slot, borra `AZURE_WEBAPP_SLOT` y el workflow vuelve a desplegar directo a producción.
