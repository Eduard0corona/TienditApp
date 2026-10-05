# Despliegue en Azure App Service

Cada push a `master` corre el workflow `Deploy WebApp (API + SPA)` (`.github/workflows/deploy-azure-webapp.yml`): compila el cliente y la solución, corre las pruebas, publica la API con la SPA en `wwwroot`, despliega el paquete y espera a que la app responda con esa versión. Las migraciones no corren al desplegar: antes del merge se aplica `database/production/<MigrationId>/apply.sql` (ver [migraciones](database-migrations.md)).

## Prueba de humo

Después de desplegar, el paso `Smoke test` consulta la app hasta 30 veces, cada 20 segundos:

- `GET /health/ready` debe responder 200 (la API arrancó y llega a la base de datos);
- `GET /` debe servir el `index.html` recién compilado (sus scripts llevan el hash del build, así que una instancia vieja no pasa).

Si en 10 minutos no se cumplen ambas, el workflow falla con el último estado de cada consulta. La versión ya quedó publicada: revisa el log stream del App Service (arranque, cadena de conexión, secretos) y la última migración aplicada. La variable de repositorio `SMOKE_TEST_URL` cambia la dirección consultada (un dominio propio); sin ella se usa la que devuelve el despliegue (`https://<app>.azurewebsites.net`).

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

## Pendiente

- Slot de staging con swap: se decidió no tenerlo por ahora (ver [flujo de Git](git-workflow.md)); con él, la prueba de humo correría contra el slot antes del swap.
- Una aprobación manual (environment `production` con revisores) antes del despliegue.
