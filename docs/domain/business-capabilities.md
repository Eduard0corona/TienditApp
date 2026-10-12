# Capabilities de negocio

## Producto actual

TienditApp soporta operación multi-tenant para negocios Food, Retail y Service. `BusinessType` define un perfil y `TenantCapability` habilita módulos combinables. Los defaults actuales son:

- Food: `Food | Inventory | Kitchen`.
- Retail: `Retail | Inventory`.
- Service: `Service | Retail | Inventory | Appointments`.

`TenantCapabilityRules` normaliza configuraciones legacy y controla catálogo, inventario, cocina, citas, unidades de producto y tipos de orden. Estos flags no son permisos de usuario.

## Capacidades observadas

- Configurar tenant, datos fiscales básicos, logo, WhatsApp y el tema de colores del negocio (Fase 16, [ADR 0010](../adr/0010-business-themes.md)): uno de ocho temas curados que ven su equipo y su menú en línea.
- Autenticar usuarios, rotar refresh tokens, confirmar email/teléfono y usar 2FA.
- Administrar membresías de empleados, roles y códigos de permiso.
- Administrar ubicaciones de inventario por sucursal (las sucursales son entidad propia, ver [sucursales](branches-and-reporting.md)).
- Gestionar categorías, productos/servicios, horarios de disponibilidad e imágenes.
- Crear órdenes retail/food y órdenes de servicio con piezas/checklist.
- Registrar pagos parciales, sesiones/movimientos de caja y cortes.
- Registrar en Vender los pedidos de Rappi, Uber Eats y DiDi Food con el folio de la app y quién cobró (Fase 22F, [ADR 0013](../adr/0013-delivery-platform-orders.md)). Incluye configurar la comisión y las retenciones de cada plataforma, ver lo que cobraron en el corte y en Inicio, y consultar las ventas por canal con el neto esperado de cada plataforma. No tiene capability ni módulo de plan: está en todos los planes, para cualquier negocio que venda productos.
- Gestionar insumos, existencias, recetas, kardex, alertas y movimientos.
- Gestionar proveedores, compras y recepciones parciales.
- Registrar clientes por tenant, perfiles registrados y temporales.
- Gestionar citas, tablero de cocina y tablero de servicio.
- Enviar/recibir WhatsApp, chat y notificaciones SignalR.
- Consultar auditoría, dashboard y reporte operativo.
- Configurar respuestas automáticas basadas en reglas/palabras clave.

## Fuera de alcance comprobado

No hay evidencia de CFDI, integración SAT, notas de crédito fiscales, motor fiscal mexicano, cuentas por cobrar completas, saldo a favor ni ledger de Wallet. `DigitalWallet` es un método de pago y un total de corte, no un monedero contable. No existe un dominio de suscripciones/churn; una categoría de plantilla llamada “Membresías” no constituye esa funcionalidad. Los pedidos de plataformas de reparto se capturan a mano, sin integración con sus APIs, y sus depósitos se concilian a mano en Reportes › Liquidaciones (Fase 22G).
