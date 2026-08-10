# Reglas de Domain

- No agregues referencias a Application, Infrastructure, WebApi, EF Core, HTTP, SignalR o providers.
- Invariantes, transiciones y comportamiento pertenecen a constructores/métodos del agregado; evita setters públicos y asignación directa de estados.
- Toda entidad tenant-owned implementa `ITenantScoped`; `TenantId` se exige al crear y no cambia.
- Expón colecciones como solo lectura y modifica mediante métodos del agregado.
- Usa `decimal` para dinero/cantidades y reglas de redondeo explícitas.
- Los eventos describen hechos y no ejecutan I/O. No introduzcas dependencias externas para publicarlos.
- Cada nueva regla incluye tests positivos y negativos en Domain.Tests.
- No edites el dominio para acomodar EF/UI sacrificando invariantes; configura materialización en Infrastructure.
