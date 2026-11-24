# ADR 0099: Acceso de soporte con motivo y límite de tiempo

- Estado: Aceptada en la Fase 32G (2026-11-24).
- Fecha: 2026-11-24
- Continúa: la auditoría del acceso de soporte de la Fase 10 (`SupportAccessAudit`, [multi-tenancy](../architecture/multi-tenancy.md)) y el contrato de errores de la [ADR 0008](0008-api-error-contract.md).

## Contexto

- **El encabezado `X-Tenant-Id` del administrador de la plataforma:** con él, un `SUPER_ADMIN` actúa en cualquier negocio, sin membresía.
  - Desde la Fase 10 cada petición así, en una ruta del negocio, queda en la actividad de ese negocio antes de ejecutarse.
  - Pero no pedía **por qué** ni **hasta cuándo**: bastaba el rol para entrar a cualquier negocio, en cualquier momento y sin fin.
- **Lo marcaban como pendiente:** `SECURITY.md`, la deuda técnica, el estado actual y el diagnóstico de multi-tenancy. Lo pedían antes de considerarlo un control entre negocios suficiente (AGENTS.md: «cross-tenant es explícito, autorizado y auditado»; «`SUPER_ADMIN` no es bypass implícito»).
- **En las rutas de la cuenta** (`/me`, `/auth` salvo `/auth/my-tenant`, `/platform`, `/public`), el encabezado cambiaba el negocio sin registrarlo.
- **El cliente nunca manda el encabezado:** los hubs lo ignoran y la consola de la plataforma pone el negocio de cada acción y la audita. Solo lo usa quien llama a la API directo.

El dueño del producto pidió avanzar con esta mejora y tomar las decisiones sin esperarlo. Tomé las de esta fase con esa autorización. **Lleva migración.**

## Decisión

1. **Un acceso de soporte es un registro** (`SupportAccess`, del negocio que abre: `ITenantScoped`):
   - el negocio y la cuenta que lo abre;
   - el **motivo**, de 10 a 500 caracteres;
   - cuándo empieza y cuándo **vence**, de 15 minutos a 4 horas después;
   - cuándo se cerró antes de tiempo.
   - Nunca se reabre ni se alarga: otro acceso es otro registro. Nada lo borra; es historial.
2. **Se abre en la consola de la plataforma, por la API** (`[RequiresPlatformAdmin]`):
   - `POST /api/v1/platform/support-access` `{ tenantId, reason, minutes }` → 201 con el acceso.
     - Responde 409 si ya hay uno abierto de esa cuenta a ese negocio, 404 si el negocio no existe y 400 si el motivo o el tiempo no caben.
   - `POST /api/v1/platform/support-access/{tenantId}/end` lo cierra antes de tiempo (204), o responde 404 si no hay uno abierto.
   - `GET /api/v1/platform/support-access` da los accesos abiertos de la cuenta, el más próximo a vencer primero.
   - Abrir y cerrar quedan en la actividad del negocio (`SupportAccessStarted` y `SupportAccessEnded`, entidad `SupportAccess`), con el motivo y el tiempo.
3. **El encabezado solo actúa con un acceso abierto** de esa cuenta a ese negocio (`TenantResolutionMiddleware` y `SupportAccessAudit`):
   - Sin acceso, o con uno cerrado: 403 `support_access_required`.
   - Con uno vencido: 403 `support_access_expired`.
   - En ninguno de los dos casos la petición corre ni se registra.
   - Un negocio que no existe sigue respondiendo 404, y si la base no puede registrar la petición, 503.
   - Cada petición que corre queda en la actividad del negocio, como desde la Fase 10, y ahora también con el acceso (`SupportAccessId`) y su motivo.
4. **En las rutas de la cuenta el encabezado ya no cambia nada:** queda el negocio de la sesión. Antes cambiaba el negocio sin registrarlo.
5. **El dueño lo ve en Seguridad y actividad:** las tres acciones se leen como «Soporte de la plataforma», «… abrió un acceso» y «… cerró su acceso», con el motivo, hasta cuándo, y el método y la ruta de cada petición. La acción registrada queda en el `title` de la celda, para el filtro.
6. **La base guarda lo que dice el dominio** (`AddSupportAccesses`):
   - restricciones del largo del motivo, de que venza después de empezar y del cierre dentro de su tiempo (de 15 minutos a 4 horas lo cuidan el dominio y el validador: una función de fechas en el check no correría en el SQLite de las pruebas de Application);
   - llaves sin cascada a `Tenants` y `AspNetUsers`;
   - el índice que lee cada petición con el encabezado.
   - Su `Down` se detiene (50066) si hay un acceso: borraría el historial.

## Consecuencias

- **Lo que logra:** cada entrada del soporte a un negocio tiene un motivo que el dueño lee, dura lo que se pidió y se puede cerrar antes. El rol solo ya no basta para entrar a un negocio.
- **Compatibilidad:**
  - **Primero `apply.sql`, después el merge.** La versión desplegada no conoce la tabla, y su encabezado sigue actuando sin acceso hasta que se despliega esta.
  - **Volver a la anterior** funciona sin `rollback.sql`: el encabezado vuelve a actuar sin acceso, como hoy.
  - **El contrato solo agrega:** dos códigos de error y tres rutas de la consola.
- **Lo que limita:**
  - **Sin pantalla en la consola:** se abre y se cierra por la API, como el encabezado, que tampoco tiene pantalla. Una sección de Plataforma para abrir accesos queda pendiente.
  - **Al dueño no le llega un aviso** (correo o WhatsApp) cuando se abre un acceso: lo ve en su actividad.
  - **Dos aperturas a la vez de la misma cuenta al mismo negocio** podrían dejar dos accesos abiertos; los dos valen y los dos se registran.
  - **Cada petición lee el acceso en la base** (un índice). Es una consulta más por petición del soporte, no de los negocios.
  - **El acceso es de la cuenta:** otro administrador necesita el suyo.
- **Decisiones por omisión** (sin preferencia expresa del dueño):
  - de 15 minutos a 4 horas;
  - un motivo de 10 a 500 caracteres;
  - uno abierto a la vez por cuenta y negocio;
  - ignorar el encabezado en las rutas de la cuenta;
  - sin aviso al dueño;
  - sin pantalla.
