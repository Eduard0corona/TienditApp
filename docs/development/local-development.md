# Desarrollo local

## Requisitos

- .NET SDK 9 para la aplicación. En el estado actual, los proyectos de tests apuntan a `net10.0` y requieren SDK 10 para ejecutarse localmente.
- Node.js y npm. El workflow usa Node.js 20; `client/package-lock.json` es la fuente de dependencias.
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

Configura secretos mediante variables de entorno o user-secrets. No copies valores de ejemplo de `docker-compose.yml`, seed o scripts a producción.

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

`./start-dev.ps1` y `start-dev.cmd` lanzan ambos procesos. Los scripts contienen credenciales seed visibles; trátalas solo como desarrollo local y cámbialas si el entorno no es desechable.

### Webhook de WhatsApp

`POST /api/v1/whatsapp/webhook` verifica la firma `X-Hub-Signature-256` con `WhatsApp:AppSecret` (App Secret de Meta) en todos los entornos y responde 503 si no está configurado. Defínelo con variable de entorno (`WhatsApp__AppSecret`) o user-secrets, nunca en `appsettings*.json`. Para probar localmente sin firmar (curl/Postman) usa `WhatsApp__AllowUnsignedWebhooks=true`: solo se respeta con `ASPNETCORE_ENVIRONMENT=Development`; en otro entorno se ignora y se registra un error.

## Docker

```powershell
docker compose up --build
```

Publica API en 5000/5001 y SQL Server en 1433. Reemplaza credenciales y JWT de ejemplo antes de cualquier entorno compartido. Detén el stack con `docker compose down`; no borres volúmenes salvo autorización explícita.

## SMS, correo y códigos OTP

Si `SmsSettings:Provider` no es `Twilio`, o faltan `AccountSid`, `AuthToken` o `FromNumber`, la API no envía el SMS. Solo con `ASPNETCORE_ENVIRONMENT=Development` registra además su cuerpo (`SMS body: ...`), que incluye el código OTP de confirmación de teléfono, para completar el flujo en local. `appsettings.json` trae `Twilio` con valores de ejemplo, así que para leer el código define un proveedor distinto (por ejemplo `SmsSettings__Provider=None`) con variable de entorno o user-secrets.

En cualquier otro entorno nunca se registran cuerpos de SMS ni códigos OTP: el aviso solo lleva el teléfono enmascarado (`***5678`). Los correos se registran con el destinatario enmascarado (`a***@e***.com`) y su cuerpo, con los enlaces de confirmación o restablecimiento, no se registra en ningún entorno. Si Twilio, Meta (WhatsApp) o el servidor SMTP rechazan un envío, fuera de Development solo se registran el estado HTTP o SMTP, el código de error del proveedor y el tipo de excepción, porque sus respuestas pueden repetir números, direcciones o contenido; en Development se registran completas para depurar.

## Validación local

```powershell
dotnet build FoodParkDigital.sln
dotnet test FoodParkDigital.sln
Set-Location client
npm run lint
npm run build
```

Si falla por infraestructura o baseline, registra comando y salida; no cambies producción para forzar verde sin diagnosticar.
