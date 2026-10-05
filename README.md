# TienditApp

TienditApp (los proyectos conservan el nombre histórico `FoodParkDigital`) es un SaaS multi-tenant para operar negocios locales. En `master` existen módulos de identidad, tenants, sucursales, empleados, catálogo, órdenes retail y de servicio, inventario, compras, caja, clientes, citas, cocina, WhatsApp, notificaciones en tiempo real, auditoría y reportes.

## Arquitectura y tecnologías

La decisión vigente es un monolito modular con Clean Architecture, DDD ligero y CQRS mediante MediatR:

- Backend: ASP.NET Core/.NET 10 (LTS), EF Core 10, SQL Server, ASP.NET Identity, JWT, refresh tokens, 2FA, FluentValidation, SignalR y Serilog.
- Frontend: React 19, TypeScript, Vite, React Router, Axios, React Hook Form y Bootstrap.
- Pruebas: xUnit en proyectos de Domain y Application. No se encontraron proyectos de pruebas de WebApi, Infrastructure ni frontend.

Consulta [la documentación](docs/README.md) para distinguir estado actual, decisiones, arquitectura objetivo, deuda y propuestas. Las reglas obligatorias para agentes y colaboradores están en [AGENTS.md](AGENTS.md) y [CONTRIBUTING.md](CONTRIBUTING.md).

## Inicio rápido

Requisitos comprobados: .NET SDK 10, Node.js/npm y SQL Server accesible. La API y las pruebas apuntan a `net10.0` (`Directory.Build.props`); los workflows usan Node.js 20 y .NET 10.

```powershell
dotnet restore FoodParkDigital.sln
dotnet run --project src/FoodParkDigital.WebApi
```

En otra terminal:

```powershell
Set-Location client
npm ci
npm run dev
```

La API usa `http://localhost:5203` y Vite `http://localhost:5173`; el proxy de Vite reenvía `/api`, `/uploads` y `/hubs` a la API. También existen `start-dev.ps1`, `start-dev.cmd` y `docker-compose.yml`. No reutilices credenciales de ejemplo fuera de desarrollo y no agregues secretos al repositorio.

## Validación

```powershell
dotnet build FoodParkDigital.sln
dotnet test FoodParkDigital.sln
Set-Location client
npm run lint
npm run build
```

Las migraciones están en `src/FoodParkDigital.Infrastructure/Persistence/Migrations`. En Development, la API aplica migraciones y seed al iniciar; producción no debe depender de ese mecanismo.

## Ramas

`master` es la única rama permanente: lo que se fusiona en ella se despliega a producción. El trabajo se hace en ramas cortas que vuelven por Pull Request (ver [flujo Git](docs/development/git-workflow.md)).
