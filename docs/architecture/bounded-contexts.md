# Contextos funcionales

Los límites son módulos lógicos dentro de un monolito y una base compartida; no son microservicios.

| Contexto | Evidencia actual | Responsabilidad actual y límites |
|---|---|---|
| Tenant y negocio | `Tenants`, `TenantSettings`, `TenantUser`, `WhatsAppConfig` | Alta/configuración del negocio, tipo, capabilities y membresía. |
| Identity y acceso | `ApplicationUser`, `ApplicationRole`, JWT, refresh token, 2FA | Autenticación global, tenant activo, roles y tokens. |
| Sucursales y empleados | `InventoryLocation`, módulo `Employees` | “Sucursal” se implementa hoy como ubicación de inventario; empleados como `TenantUser` + Identity. No existe agregado `Branch`. |
| Catálogo | `Category`, `Product`, templates | Productos/servicios, unidades, disponibilidad, imágenes y agenda de disponibilidad. |
| Órdenes y ventas | `Order`, items, pagos, state machines | Creación, cliente, importes, estados, cancelación y venta retail/food. |
| Servicio | `ServiceOrderDetail`, `ServiceOrderPiece`, service board | Recepción, checklist/piezas, asignación y transiciones propias. |
| Inventario | `SupplyItem`, `InventoryStock`, movimientos, recetas | Insumos, existencias por ubicación, kardex, alertas y consumo por orden. |
| Compras | `Supplier`, `PurchaseOrder` | Proveedores, órdenes de compra, recepción parcial/total y entrada de stock. |
| Caja y pagos | `OrderPayment`, `CashSession`, `CashTransaction` | Pagos parciales, sesiones, movimientos y cortes; vínculo opcional a ubicación/orden. |
| Clientes | `Customer`, `CustomerProfile`, `TemporaryCustomer` | Clientes legacy por tenant, perfil registrado global y cliente temporal reclamable. |
| Citas | `Appointment` | Agenda y estados de citas por tenant. |
| Cocina/fulfillment | `KitchenController`, `GetKitchenOrdersQuery`, `OrderHub` | Vista y avances de preparación para capability Kitchen. |
| WhatsApp/chat/notificaciones | conversaciones, mensajes, webhook, servicios y hubs | Mensajería, chat, plantillas y notificación SignalR. |
| Auditoría y reportes | `AuditLog`, dashboards, reportes operativos | Trazabilidad parcial y agregaciones de operación. |
| Agente de respuestas | `AiAgent`, `AiAgentAutoReplyService` | Configura mensajes y responde por coincidencia de palabras clave. No llama a un LLM. |
| Marketing | `SendPromotionalMessageCommand` | Envío promocional; su superficie es pequeña y depende de WhatsApp. |

## Reglas entre contextos

- Cruces que cambian varias áreas se coordinan desde un caso de uso Application.
- Identificadores compartidos no autorizan acceso; todas las consultas preservan tenant y permisos.
- Integraciones consumen puertos, eventos o handlers; las entidades no llaman providers.
- La base compartida no permite joins cross-tenant implícitos.
- “Membresías” aparece como categoría de plantilla y `TenantUser` se describe como membership técnica; no hay evidencia de un dominio de suscripciones. No se incluye churn/cancelación de membresías.
