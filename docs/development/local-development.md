# Desarrollo local

## Requisitos

- .NET SDK 10: la API y las pruebas apuntan a `net10.0` desde `Directory.Build.props`.
- Node.js y npm. Los workflows usan Node.js 20 (Vite 7 requiere 20.19 o posterior); `client/package-lock.json` es la fuente de dependencias.
- SQL Server; `docker-compose.yml` ofrece SQL Server 2022 y la API.
- Opcional: Docker Desktop y `dotnet-ef` para administrar migraciones manualmente.

No hay `global.json`; el SDK más reciente compatible instalado puede gobernar los comandos de solución.

## Instalación y ejecución

```powershell
dotnet restore FoodParkDigital.sln
Set-Location client
npm ci
Set-Location ..
```

### Configuración y secretos

El repositorio no contiene credenciales. `appsettings.Development.json` solo trae ajustes no sensibles; cada desarrollador configura sus valores con user-secrets (el proyecto WebApi tiene `UserSecretsId`) o con un archivo local `appsettings.Development.local.json`, que está en `.gitignore`.

```powershell
dotnet user-secrets --project src/FoodParkDigital.WebApi set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=TienditApp;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
dotnet user-secrets --project src/FoodParkDigital.WebApi set "Seed:SuperAdminPassword" "<contraseña local>"
# Opcional en Development: sin esta llave se usa una efímera y los tokens se invalidan al reiniciar.
dotnet user-secrets --project src/FoodParkDigital.WebApi set "JwtSettings:Secret" "<64 caracteres aleatorios o más>"
```

- `JwtSettings:Secret` es obligatorio fuera de Development: la API no arranca si falta, si tiene menos de 32 bytes o si es una llave que estuvo publicada en el repositorio.
- En Development, la migración y el seed automáticos se controlan con `Database:AutoMigrateOnStartup` y `Database:SeedOnStartup` (activos en `appsettings.Development.json`). Solo se ejecutan si el servidor es local (loopback, LocalDB o un nombre de equipo/servicio sin dominio). Para una base remota hay que activar `Database:AllowRemoteDatabase=true` de forma explícita.
- El seed crea el SUPER_ADMIN `admin@foodparkdigital.com` (configurable con `Seed:SuperAdminEmail`) solo si existe `Seed:SuperAdminPassword`. Si tu base ya tenía ese usuario con la contraseña anterior publicada, cámbiala.

No copies valores de ejemplo de `docker-compose.yml` a producción.

Terminal de API:

```powershell
dotnet run --project src/FoodParkDigital.WebApi
```

Terminal de frontend:

```powershell
Set-Location client
npm run dev
```

Direcciones comprobadas en configuración: API HTTP `http://localhost:5203`, HTTPS `https://localhost:7170` y Vite `http://localhost:5173`. Swagger solo se habilita en Development.

`./start-dev.ps1` y `start-dev.cmd` lanzan ambos procesos. La contraseña del SUPER_ADMIN es la que definas en `Seed:SuperAdminPassword`.

### Webhook de WhatsApp

`POST /api/v1/whatsapp/webhook` verifica la firma `X-Hub-Signature-256` con `WhatsApp:AppSecret` (App Secret de Meta) en todos los entornos y responde 503 si no está configurado. Defínelo con variable de entorno (`WhatsApp__AppSecret`) o user-secrets, nunca en `appsettings*.json`. Para probar localmente sin firmar (curl/Postman) usa `WhatsApp__AllowUnsignedWebhooks=true`: solo se respeta con `ASPNETCORE_ENVIRONMENT=Development`; en otro entorno se ignora y se registra un error.

Una entrega firmada recibe 200 cuando se procesó, o cuando se descarta por JSON inválido o `PhoneNumberId` desconocido. Recibe 500 si algún mensaje o la actualización de estados no se pudo procesar, para que Meta la reenvíe; al reenviarla, los mensajes ya guardados se ignoran por `ExternalMessageId`.

## Docker

```powershell
$env:SEED_SUPERADMIN_PASSWORD = "<contraseña local>"
docker compose up --build
```

La imagen compila también el SPA y corre con el usuario no root de la imagen de .NET. Publica API en 5000/5001 y SQL Server en 1433. Variables opcionales: `JWT_SECRET` (sin ella se usa una llave efímera de desarrollo), `SEED_SUPERADMIN_PASSWORD` y `MSSQL_SA_PASSWORD` (la contraseña por defecto del contenedor SQL solo sirve para un entorno local desechable). Detén el stack con `docker compose down`; no borres volúmenes salvo autorización explícita.

## SMS, correo y códigos OTP

Si `SmsSettings:Provider` no es `Twilio`, o faltan `AccountSid`, `AuthToken` o `FromNumber`, la API no envía el SMS. Solo con `ASPNETCORE_ENVIRONMENT=Development` registra además su cuerpo (`SMS body: ...`), que incluye el código OTP de confirmación de teléfono, para completar el flujo en local. `appsettings.json` trae `Twilio` con valores de ejemplo, así que para leer el código define un proveedor distinto (por ejemplo `SmsSettings__Provider=None`) con variable de entorno o user-secrets.

En cualquier otro entorno nunca se registran cuerpos de SMS ni códigos OTP: el aviso solo lleva el teléfono enmascarado (`***5678`). Los correos se registran con el destinatario enmascarado (`a***@e***.com`) y su cuerpo, con los enlaces de confirmación o restablecimiento, no se registra en ningún entorno. Si Twilio, Meta (WhatsApp) o el servidor SMTP rechazan un envío, fuera de Development solo se registran el estado HTTP o SMTP, el código de error del proveedor y el tipo de excepción, porque sus respuestas pueden repetir números, direcciones o contenido; en Development se registran completas para depurar.

Al resolver el cliente de un pedido, los registros `OrderCustomerLinked` y `CreatedTempCustomer` siguen la misma regla: fuera de Development llevan el teléfono enmascarado (`***5678`) junto al usuario o al cliente temporal, y solo Development registra el número completo.

## Errores no controlados

`GlobalExceptionHandlerMiddleware` responde con `ProblemDetails`. Incluye `traceId`, el mismo valor que lleva la entrada del log, para que soporte encuentre el error que reporta un usuario. Las respuestas 500 no incluyen el mensaje de la excepción. El log registra el tipo (`ErrorType`), el número de error de SQL Server (`SqlErrorNumber`) y el `TraceId`.

SQL Server copia valores de las filas en sus mensajes: la clave duplicada de un índice único (teléfono, email) o el inicio de un valor truncado. Por eso, fuera de Development, si la cadena de la excepción contiene una excepción de base de datos, se registran los tipos, el número de error y el stack trace, pero ningún mensaje (`LogRedaction.RedactDatabaseMessages`). Las demás excepciones se registran completas. El webhook de WhatsApp aplica la misma regla a los errores que captura.

Fuera de Development, EF Core tampoco registra por su cuenta los guardados ni las consultas fallidos (`SaveChangesFailed`, `QueryIterationFailed`, `ExecuteUpdateFailed`, `ExecuteDeleteFailed`, `NonQueryOperationFailed`): la excepción sigue propagándose y quien la maneja la registra redactada. Development conserva la excepción completa en todos los casos para depurar.

## Validación local

```powershell
dotnet build FoodParkDigital.sln
dotnet test FoodParkDigital.sln
Set-Location client
npm run lint
npm run build
```

El workflow `CI` (`.github/workflows/ci.yml`) ejecuta lo mismo en cada PR, además de `dotnet ef migrations has-pending-model-changes`, `npm audit --omit=dev --audit-level=high` y el job `Migrations`, que aplica las migraciones con `sqlcmd` en SQL Server 2022 (ver [migraciones](database-migrations.md)).

Si falla por infraestructura o baseline, registra comando y salida; no cambies producción para forzar verde sin diagnosticar.
