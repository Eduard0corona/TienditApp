# Despliegue en Azure App Service

Cada push a `master` corre el workflow `Deploy WebApp (API + SPA)` (`.github/workflows/deploy-azure-webapp.yml`): compila el cliente y la solución, corre las pruebas, publica la API con la SPA en `wwwroot`, despliega el paquete y espera a que la app responda con esa versión. Las migraciones no corren al desplegar: antes del merge se aplica `database/production/<MigrationId>/apply.sql` (ver [migraciones](database-migrations.md)). Sin más configuración el paquete va directo a la app; con un [slot de staging](#slot-de-staging-con-swap-y-aprobación) va primero al slot y pasa a producción con un swap aprobado.

## Prueba de humo

Después de desplegar, `.github/scripts/smoke-test.sh` consulta la app hasta 30 veces, cada 20 segundos:

- `GET /health/ready` debe responder 200 (la API arrancó y llega a la base de datos);
- `GET /health/schema` debe decir `Healthy`: la base tiene todas las migraciones de esta versión. Si falta un `apply.sql`, dice `Unhealthy` (503) y el workflow falla pidiendo aplicarlo; la app sigue atendiendo, porque ese chequeo no forma parte de `/health/ready`;
- `GET /` debe servir el `index.html` recién compilado (sus scripts llevan el hash del build, así que una instancia vieja no pasa);
- desde la Fase 20, ese script, pedido como lo pide un navegador (`Accept-Encoding: br, gzip`), debe llegar con `Content-Encoding: br` y `Cache-Control: ... immutable` (ver [entrega del cliente](#entrega-del-cliente-comprimida-y-en-caché)).

Si en 10 minutos no se cumplen las cuatro, el workflow falla con el último estado de cada consulta. Sin slot, la versión ya quedó publicada: revisa el log stream del App Service (arranque, cadena de conexión, secretos; el chequeo del esquema escribe ahí qué migración falta) y aplica el `apply.sql` pendiente. La variable de repositorio `SMOKE_TEST_URL` cambia la dirección de producción consultada (un dominio propio); sin ella se usa la que devuelve el despliegue (`https://<app>.azurewebsites.net`).

## Entrega del cliente comprimida y en caché

Desde la Fase 20 la API entrega el cliente así (`src/FoodParkDigital.WebApi/Http/ClientDelivery.cs`, probado en `ClientDeliveryTests`):

- **Copias comprimidas del publish.** Al publicar, el SDK de .NET deja junto a cada script y estilo de `client/dist` una copia Brotli (`.br`, a su máxima calidad) y una gzip (`.gz`): es la compresión de static web assets, que el csproj deja encendida (`CompressionEnabled`). La API manda la copia que el navegador acepta (Brotli primero) como el archivo que codifica, con `Content-Encoding` y `Vary: Accept-Encoding`; no comprime nada en cada petición, así que no gasta CPU. Una copia pedida por su nombre responde 404.
- **Solo `/assets` se comprime.** Las respuestas de la API llevan datos de una sesión y nunca se comprimen: su tamaño no sirve para adivinarlos (BREACH).
- **Caché.** Los archivos de `/assets` llevan el hash de su contenido en el nombre, así que una versión nueva nunca reusa un nombre: van con `Cache-Control: public, max-age=31536000, immutable` y el navegador no vuelve a preguntar por ellos. `index.html`, que sirve cualquier ruta de la app, va con `no-cache`: el navegador lo revisa en cada visita y un despliegue se ve de inmediato.
- **Archivos que ya no existen.** Un archivo de `/assets` que no está responde 404, nunca el `index.html`: una página abierta antes del despliegue lo nota y carga la versión nueva (`utils/chunkReload.ts`). Las páginas abiertas que no piden nada nuevo ven el aviso "Hay una versión nueva de TienditApp" (`NewVersionNotice`), que revisa el `index.html` al volver a la pestaña y cada 10 minutos.

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
- `StopMappingRequireOpenCashSession` no cambia la base: la versión nueva deja de mapear `TenantSettings.RequireOpenCashSession` (nadie la lee: todo cobro exige una caja abierta). La columna se queda con su valor por omisión, 0, que toman las filas nuevas; una fase posterior la borra cuando ninguna versión desplegada la mapee. Es el mismo paso que hizo la PR #47 en master (sección anterior).
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
