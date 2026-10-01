# Deuda técnica

Actualizada el 2026-10-01 tras la auditoría del proyecto y sus correcciones.

## Crítica antes de producción

1. Credenciales expuestas: cadenas de conexión de Azure SQL y el secreto JWT estuvieron versionados y siguen en el historial de git. El código ya no los contiene y la API rechaza el secreto publicado, pero hay que rotarlos, revisar el firewall de Azure SQL y verificar si la cuenta SUPER_ADMIN sembrada con la contraseña anterior existe en la base remota. Reescribir el historial es destructivo y requiere coordinación.
2. En corrección en tareas separadas: la fuga entre tenants del dashboard (productos más vendidos con `IgnoreQueryFilters()`), el escalamiento a `SUPER_ADMIN` mediante `POST /api/v1/auth/register`, el webhook de WhatsApp que acepta peticiones sin firma cuando falta `AppSecret` (y `PhoneNumberId`/`ExternalMessageId` no únicos), el 2FA que bloquea la cuenta, la subida de imagen de producto que borra sus campos y las escrituras parciales del registro de negocio/cliente y de la confirmación de teléfono.
3. Aislamiento incompleto: `TenantSettings`/`WhatsAppConfig` no implementan `ITenantScoped`; `OrderItem`/`OrderStatusHistory` no tienen `TenantId`; quedan usos de `IgnoreQueryFilters()` por revisar; el override `X-Tenant-Id` de `SUPER_ADMIN` solo se audita en el log.
4. Falta una suite que pruebe aislamiento HTTP, EF contra SQL Server, archivos, SignalR y webhooks.
5. Archivos: el contenido se valida por firma y las fotos se re-codifican, pero `/uploads` es estático y público (las evidencias de pago se descargan sin autorización) y los PDF no pasan por un escaneo de malware.
6. AutoMapper 13.0.1 tiene un advisory alto (`GHSA-rvv3-g6hj-g44x`, DoS por recursión) que solo se corrige en 15.1.1+/16.1.1+, versiones con licencia comercial. Hay que decidir entre licenciar, migrar a mapeo manual u otra librería. El riesgo práctico actual es bajo: solo mapea entidades de la base con profundidad acotada.
7. .NET 9 dejó de tener soporte en mayo de 2026: planear la migración de la API a .NET 10 (LTS) junto con EF Core y ASP.NET Core 10.

## Alta

- Eventos se limpian/publican después del commit sin Outbox; side effects pueden perderse o duplicarse.
- Impuesto fijo `subtotal × 16%`, sin desglose fiscal persistido y sin decidir si los precios del catálogo incluyen IVA (si lo incluyen, se sobrecobra).
- `SUPER_ADMIN` y `TENANT_ADMIN` omiten permisos/capabilities en partes del middleware; el claim `role` es el rol global y no el de la membresía; autorización distribuida por strings de ruta.
- Sesiones: los access tokens no se pueden revocar antes de expirar (no se valida el security stamp), los refresh tokens se guardan en texto plano sin detección de reuso, no hay bloqueo de cuenta y el login distingue cuentas sin confirmar o bloqueadas.
- Folios de órdenes y movimientos por conteo (una colisión responde 500); la recepción de compra no tiene idempotency key estable.
- No existen reembolsos: una orden con pagos no se puede cancelar.
- Contratos de error mezclan `ProblemDetails` con objetos `{ error }`.
- Controllers no propagan `CancellationToken`.
- Política de contraseña (longitud mínima 6) pendiente de evaluación.
- `OrderItem.Quantity` es entero aunque existen unidades Kg, Litro, Metro y Hora.

## Media

- Controllers grandes; `WhatsAppWebhookController` accede al DbContext directamente.
- Componentes frontend de 700–2,450 líneas y `types/index.ts` monolítico.
- Sin pruebas frontend ni de integración con SQL Server.
- Producción .NET 9 y tests `net10.0` (el CI instala ambos SDK); no hay `global.json`.
- `PendingModelChangesWarning` se ignora en ejecución; el CI lo valida con `has-pending-model-changes`.
- Auto-migrate/seed solo existe en Development contra un servidor local, pero falta el proceso productivo de migraciones.
- Listados pequeños aún materializan colecciones sin paginación (categorías, AI agents, ubicaciones, kitchen).
- El dashboard carga en memoria todas las órdenes del rango y su ticket promedio incluye órdenes canceladas y pendientes.
- Consultas con varias colecciones incluidas no usan `AsSplitQuery`.
- `client/README.md` sigue siendo template genérico.
- Pago puede persistirse sin movimiento de caja cuando no hay sesión abierta; solo hay una caja abierta por tenant, no por sucursal.
- “AI Agent” conserva campos de modelo/temperatura/memoria aunque la implementación es por palabras clave, lo que puede inducir a error.
- `Product.StockQuantity` es informativo: las ventas no lo descuentan.
- Un administrador puede activar cualquier capability desde `PUT /auth/my-tenant`; es un bypass si los módulos dependen del plan contratado.
- Las actions de los workflows se fijan por tag y no por SHA. Los workflows que solo existen en `master` no se revisaron en esta rama.
- La búsqueda de `DataTable` filtra solo la página cargada; faltan etiquetas asociadas y nombres accesibles en botones de icono.

## Resuelto el 2026-10-01

- Secretos fuera del repositorio (user-secrets/variables de entorno), llave JWT obligatoria fuera de Development y auto-migración/seed limitados a Development con servidor local.
- CORS por allowlist, cabeceras de seguridad, HSTS, cookie de refresh `Secure` fuera de Development y OTP fuera de los logs de producción.
- Membresía revalidada en "mi tenant", marketing, hubs y `TenantOrdersController`; interceptor que rechaza `TenantId` ajeno o modificado; exclusiones del middleware por prefijo; rol `SUPERADMIN` corregido; override de `SUPER_ADMIN` auditado; refresh token rotado al cambiar de tenant.
- Topes de cantidades, piezas y paginación.
- Caja: signo por tipo de movimiento, efectivo separado de otros métodos, una caja abierta por tenant, token de concurrencia y fechas del servidor.
- Órdenes: cancelación unificada y bloqueada con pagos, cocina con órdenes del POS, consumo de piezas en el mismo guardado, órdenes mixtas, concurrencia entre piezas.
- Inventario y compras: costo promedio solo por entradas, referencias reservadas, recepciones sin líneas repetidas, recetas que cambian de insumo y tokens de concurrencia.
- Reporte operativo sin IVA en la utilidad, costo de ventas de órdenes reconocidas y mes del tenant.
- Uploads validados por firma y re-codificados, sin metadatos, con tope de píxeles; evidencias limitadas a archivos del tenant.
- Frontend: sesión expirada, refresh entre pestañas, cambio de tenant, SignalR, IVA en el carrito, acciones de estado/cancelación, caja, chat, reset de contraseña, página de inicio por permisos y error de lint `prefer-const`.
- CI con build, tests, migraciones, lint, build y auditoría; despliegue condicionado a tests y lint; imagen Docker con SPA y usuario no root; npm sin vulnerabilidades y SQLitePCLRaw eliminado.

## Decisiones humanas pendientes

- Modelo de autorización definitivo: policies/resource handlers, rol por tenant en el token y alcance administrativo cross-tenant.
- Política fiscal/redondeo, si los precios incluyen IVA y responsable legal-contable.
- Semántica cuando no hay caja abierta, caja por sucursal y diseño de reembolsos/ajustes/CxC/Wallet.
- Estrategia para reconciliar `master`/`develop` y migraciones.
- Versión objetivo de .NET (9 ya sin soporte) y del SDK de tests.
- Prioridad de Outbox frente a integraciones críticas.
- AutoMapper: licencia comercial o reemplazo.
- Cantidades fraccionarias y control de stock por producto.
- Capabilities ligadas al plan contratado.
