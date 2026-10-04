# Capabilities de negocio

## Producto actual

TienditApp soporta operación multi-tenant para negocios Food, Retail y Service. `BusinessType` define un perfil y `TenantCapability` habilita módulos combinables. Los defaults actuales son:

- Food: `Food | Inventory | Kitchen`.
- Retail: `Retail | Inventory`.
- Service: `Service | Retail | Inventory | Appointments`.

`TenantCapabilityRules` normaliza configuraciones legacy y controla catálogo, inventario, cocina, citas, unidades de producto y tipos de orden. Estos flags no son permisos de usuario.

## Capacidades observadas

- Configurar tenant, datos fiscales básicos, logo y WhatsApp.
- Autenticar usuarios, rotar refresh tokens, confirmar email/teléfono y usar 2FA.
- Administrar membresías de empleados, roles y códigos de permiso.
- Administrar ubicaciones de inventario por sucursal (las sucursales son entidad propia, ver [sucursales](branches-and-reporting.md)).
- Gestionar categorías, productos/servicios, horarios de disponibilidad e imágenes.
- Crear órdenes retail/food y órdenes de servicio con piezas/checklist.
- Registrar pagos parciales, sesiones/movimientos de caja y cortes.
- Gestionar insumos, existencias, recetas, kardex, alertas y movimientos.
- Gestionar proveedores, compras y recepciones parciales.
- Registrar clientes por tenant, perfiles registrados y temporales.
- Gestionar citas, tablero de cocina y tablero de servicio.
- Enviar/recibir WhatsApp, chat y notificaciones SignalR.
- Consultar auditoría, dashboard y reporte operativo.
- Configurar respuestas automáticas basadas en reglas/palabras clave.

## Fuera de alcance comprobado

No hay evidencia de CFDI, integración SAT, notas de crédito fiscales, motor fiscal mexicano, cuentas por cobrar completas, saldo a favor ni ledger de Wallet. `DigitalWallet` es un método de pago y un total de corte, no un monedero contable. No existe un dominio de suscripciones/churn; una categoría de plantilla llamada “Membresías” no constituye esa funcionalidad.
