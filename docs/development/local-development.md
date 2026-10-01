# Desarrollo local

## Requisitos

- .NET SDK 9 para la aplicación. En el estado actual, los proyectos de tests apuntan a `net10.0` y requieren SDK 10 para ejecutarse localmente.
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

## Docker

```powershell
$env:SEED_SUPERADMIN_PASSWORD = "<contraseña local>"
docker compose up --build
```

La imagen compila también el SPA y corre con el usuario no root de la imagen de .NET. Publica API en 5000/5001 y SQL Server en 1433. Variables opcionales: `JWT_SECRET` (sin ella se usa una llave efímera de desarrollo), `SEED_SUPERADMIN_PASSWORD` y `MSSQL_SA_PASSWORD` (la contraseña por defecto del contenedor SQL solo sirve para un entorno local desechable). Detén el stack con `docker compose down`; no borres volúmenes salvo autorización explícita.

## Validación local

```powershell
dotnet build FoodParkDigital.sln
dotnet test FoodParkDigital.sln
Set-Location client
npm run lint
npm run build
```

El workflow `CI` (`.github/workflows/ci.yml`) ejecuta lo mismo en cada PR, además de `dotnet ef migrations has-pending-model-changes` y `npm audit --omit=dev --audit-level=high`.

Si falla por infraestructura o baseline, registra comando y salida; no cambies producción para forzar verde sin diagnosticar.
