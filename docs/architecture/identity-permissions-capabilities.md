# Identidad, roles, permisos y capabilities

## Conceptos separados

- **Rol:** clasificación general del usuario (`SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF`, `KITCHEN`, `CUSTOMER`) y rol de membresía (`Owner`, `Admin`, etc.).
- **Permiso:** acción específica del usuario dentro del tenant, almacenada como códigos en `TenantUser.PermissionCodes`. El catálogo actual incluye catálogo, órdenes, tablero, inventario, caja, clientes, reportes, WhatsApp, agente, empleados y citas.
- **Capability:** módulo habilitado al tenant (`Retail`, `Service`, `Food`, `Inventory`, `Appointments`, `Kitchen`).

Un capability no concede permiso y un permiso no habilita un capability. La autorización efectiva requiere identidad válida, acceso activo al tenant, capability del módulo y permiso/rol adecuados.

## Estado actual

ASP.NET Identity autentica y el JWT incluye contexto del tenant. `TenantAccessService` resuelve membresía, capabilities normalizadas y permisos; si no hay permisos explícitos usa defaults por rol. `TenantResolutionMiddleware` aplica permisos/capabilities por prefijos de ruta y los controllers además usan `[Authorize]` y, con frecuencia, `[Authorize(Roles=...)]`. El frontend usa `ProtectedRoute` y `CapabilityRoute` para UX.

## Guardrails

- Los guards de React y menús ocultos no son seguridad.
- Cada operación sensible se valida en backend, preferentemente cerca del caso de uso además del middleware.
- Revisa acceso al objeto, no solo acceso al endpoint.
- `SUPER_ADMIN` requiere control cross-tenant explícito; nunca se interpreta como permiso universal implícito.
- Los códigos desconocidos se rechazan/normalizan mediante `TenantPermissionCatalog`.
- Cambios de rol, permiso o capability se auditan y deben invalidar o limitar sesiones/tokens según riesgo.
- Rutas nuevas deben declarar requisitos de permiso/capability sin depender de listas frágiles por string.

## Deuda actual

- Muchos controllers dependen de roles aunque ya existe un modelo granular; el middleware mitiga parte, pero la política está dispersa.
- `TENANT_ADMIN` y `SUPER_ADMIN` omiten comprobaciones granulares en el middleware.
- El mapeo por prefijo y método no expresa recursos ni condiciones de negocio.
- `OrderHub.JoinKitchenGroup` compara `SUPERADMIN`, diferente de `SUPER_ADMIN`.
- No se encontró cobertura API end-to-end para la matriz rol/permiso/capability/tenant.
