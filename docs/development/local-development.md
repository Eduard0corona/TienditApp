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

## Docker

```powershell
docker compose up --build
```

Publica API en 5000/5001 y SQL Server en 1433. Reemplaza credenciales y JWT de ejemplo antes de cualquier entorno compartido. Detén el stack con `docker compose down`; no borres volúmenes salvo autorización explícita.

## Validación local

```powershell
dotnet build FoodParkDigital.sln
dotnet test FoodParkDigital.sln
Set-Location client
npm run lint
npm run build
```

Si falla por infraestructura o baseline, registra comando y salida; no cambies producción para forzar verde sin diagnosticar.
