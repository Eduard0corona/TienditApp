# Contextos funcionales

Los límites son módulos lógicos dentro de un monolito y una base compartida; no son microservicios.

| Contexto | Evidencia actual | Responsabilidad actual y límites |
|---|---|---|
| Tenant y negocio | `Tenants`, `TenantSettings`, `TenantUser`, `WhatsAppConfig` | Alta/configuración del negocio, tipo, capabilities y membresía. |
| Identity y acceso | `ApplicationUser`, `ApplicationRole`, JWT, refresh token, 2FA | Autenticación global, tenant activo, roles y tokens. |
| Sucursales y empleados | `Branch`, módulo `Employees` | Desde la Fase 3 la sucursal es un agregado propio (`Branch`): ventas (`Order.BranchId`), cajas (`CashSession.BranchId`) y ubicaciones de inventario (`InventoryLocation.BranchId`) le pertenecen. Desde la Fase 22A dice cómo atiende (`ServiceMode`): una sucursal solo a domicilio no publica su dirección ([ADR 0016](../adr/0016-delivery-only-branches.md)). Empleados como `TenantUser` + Identity. Ver [sucursales y reportes](../domain/branches-and-reporting.md). |
| Catálogo | `Category`, `Product`, templates; desde la Fase 22F-2, `ProductPlatformPrice` y `ProductOptionPlatformPrice` | Productos/servicios, unidades, disponibilidad, imágenes y agenda de disponibilidad. El precio de cada producto y opción en la app de cada plataforma de reparto, que solo usan los pedidos de esa plataforma ([ADR 0014](../adr/0014-delivery-platform-prices.md)). |
| Órdenes y ventas | `Order`, items, pagos, state machines; desde la Fase 22F, `DeliveryPlatformSetting` y `PlatformOrderDetail`; desde la Fase 22G, `PlatformSettlement` | Creación, cliente, importes, estados, cancelación y venta retail/food. Pedidos de Rappi, Uber Eats y DiDi Food con el folio de la app, quién cobró y la comisión y el neto esperado que copian de la configuración de su plataforma ([ADR 0013](../adr/0013-delivery-platform-orders.md)). La liquidación de cada depósito de una plataforma con los pedidos que paga y los ajustes que explican el resto; no es un movimiento de caja ([ADR 0015](../adr/0015-delivery-platform-settlements.md)). |
| Entregas | `DeliveryZone` y sus `DeliveryZonePostalCode` (Fase 22B) | A dónde entrega el negocio y cuánto cobra, por código postal: costo de envío, pedido mínimo y envío gratis desde un monto ([ADR 0017](../adr/0017-delivery-zones.md)). Hoy lo usan las respuestas automáticas; el pedido a domicilio lo cobrará en la Fase 22C. |
| Servicio | `ServiceOrderDetail`, `ServiceOrderPiece`, service board | Recepción, checklist/piezas, asignación y transiciones propias. |
| Inventario | `SupplyItem`, `InventoryStock`, movimientos, recetas | Insumos, existencias por ubicación, kardex, alertas y consumo por orden. |
| Compras | `Supplier`, `PurchaseOrder` | Proveedores, órdenes de compra, recepción parcial/total y entrada de stock. |
| Caja y pagos | `OrderPayment`, `CashSession`, `CashTransaction` | Pagos parciales, sesiones, movimientos y cortes; vínculo opcional a ubicación/orden. El pago «Plataforma» (lo que cobró una plataforma de reparto) no entra a ninguna caja. |
| Clientes | `Customer`, `CustomerProfile`, `TemporaryCustomer` | Clientes legacy por tenant, perfil registrado global y cliente temporal reclamable. |
| Citas | `Appointment` | Agenda y estados de citas por tenant. |
| Cocina/fulfillment | `KitchenController`, `GetKitchenOrdersQuery`, `OrderHub` | Vista y avances de preparación para capability Kitchen. |
| WhatsApp/chat/notificaciones | conversaciones, mensajes, webhook, servicios y hubs | Mensajería, chat, plantillas y notificación SignalR. Las plantillas llevan el estado en español (`OrderStatusLabels`) y montos con formato fijo ($1,234.50). |
| Auditoría y reportes | `AuditLog`, dashboards, reportes operativos, `DailySalesSnapshot` | Trazabilidad parcial y agregaciones de operación; ventas diarias por sucursal con días cerrados guardados como modelo de lectura; ventas por canal en vivo y pedidos por plataforma (Fase 22F); lo que cada plataforma debe y sus liquidaciones en Reportes › Liquidaciones (Fase 22G). |
| Agente de respuestas | `AiAgent`, `AiAgentAutoReplyService` | Configura mensajes y responde por coincidencia de palabras clave. No llama a un LLM. |
| Marketing | `SendPromotionalMessageCommand` | Envío promocional; su superficie es pequeña y depende de WhatsApp. |
| Plataforma y facturación | `Plan`, `TenantSubscription`, `SubscriptionPayment`, consola `api/v1/platform` | Planes con módulos y límites, suscripción por negocio (prueba, al corriente, vencido, suspendido, cancelado) y pagos registrados por la plataforma. Ver [planes y facturación](../domain/billing-and-platform.md). |

## Reglas entre contextos

- Cruces que cambian varias áreas se coordinan desde un caso de uso Application.
- Identificadores compartidos no autorizan acceso; todas las consultas preservan tenant y permisos.
- Integraciones consumen puertos, eventos o handlers; las entidades no llaman providers.
- La base compartida no permite joins cross-tenant implícitos.
- “Membresías” aparece como categoría de plantilla y `TenantUser` se describe como membership técnica. Las suscripciones de los negocios a la plataforma son el contexto “Plataforma y facturación”; no hay membresías de clientes finales.
