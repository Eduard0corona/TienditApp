# Despliegue en Azure App Service

Cada push a `master` corre el workflow `Deploy WebApp (API + SPA)` (`.github/workflows/deploy-azure-webapp.yml`): compila el cliente y la solución, corre las pruebas, publica la API con la SPA en `wwwroot`, despliega el paquete y espera a que la app responda con esa versión. Las migraciones no corren al desplegar: antes del merge se aplica `database/production/<MigrationId>/apply.sql` (ver [migraciones](database-migrations.md)). Sin más configuración el paquete va directo a la app; con un [slot de staging](#slot-de-staging-con-swap-y-aprobación) va primero al slot y pasa a producción con un swap aprobado.

## Prueba de humo

Después de desplegar, `.github/scripts/smoke-test.sh` consulta la app hasta 30 veces, cada 20 segundos:

- `GET /health/ready` debe responder 200 (la API arrancó y llega a la base de datos);
- `GET /health/schema` debe decir `Healthy`: la base tiene todas las migraciones de esta versión. Si falta un `apply.sql`, dice `Unhealthy` (503) y el workflow falla pidiendo aplicarlo; la app sigue atendiendo, porque ese chequeo no forma parte de `/health/ready`;
- `GET /` debe servir el `index.html` recién compilado (sus scripts llevan el hash del build, así que una instancia vieja no pasa).

Si en 10 minutos no se cumplen las tres, el workflow falla con el último estado de cada consulta. Sin slot, la versión ya quedó publicada: revisa el log stream del App Service (arranque, cadena de conexión, secretos; el chequeo del esquema escribe ahí qué migración falta) y aplica el `apply.sql` pendiente. La variable de repositorio `SMOKE_TEST_URL` cambia la dirección de producción consultada (un dominio propio); sin ella se usa la que devuelve el despliegue (`https://<app>.azurewebsites.net`).

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
