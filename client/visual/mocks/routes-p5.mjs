// Phase 4E endpoints for the fake backend: team invitations, named roles, SMS recovery and phone change.
import { NOW_ISO } from './data.mjs';

const PERMISSION_OPTIONS = [
  { code: 'ManageOrders', name: 'Tomar pedidos', description: 'Vender, levantar pedidos y cuentas de mesa, y cambiar su estado.' },
  { code: 'ChargeOrders', name: 'Cobrar', description: 'Registrar pagos y cerrar ventas. Sin este permiso, los pedidos quedan para que los cobre alguien más.' },
  { code: 'ManageCash', name: 'Caja', description: 'Abrir y cerrar su caja, gastos, retiros y cortes.' },
  { code: 'ManageServiceBoard', name: 'Cocina y taller', description: 'Ver las comandas y los trabajos del taller y marcarlos listos.' },
  { code: 'ManageCatalog', name: 'Catálogo', description: 'Productos, categorías y servicios.' },
  { code: 'ManageInventory', name: 'Inventario', description: 'Insumos, existencias, compras y proveedores.' },
  { code: 'ManageCustomers', name: 'Clientes', description: 'Clientes del negocio, sus notas y direcciones.' },
  { code: 'ManageAppointments', name: 'Agenda', description: 'Citas y reservaciones.' },
  { code: 'ManageWhatsApp', name: 'Mensajes', description: 'Chat de WhatsApp con los clientes.' },
  { code: 'ManageAiAgent', name: 'Respuestas automáticas', description: 'Configurar las respuestas automáticas de WhatsApp.' },
  { code: 'ManageCampaigns', name: 'Campañas y recordatorios', description: 'Plantillas, campañas por WhatsApp y recordatorios de citas.' },
  { code: 'ViewReports', name: 'Reportes', description: 'Inicio, reportes y ventas por sucursal.' },
  { code: 'ProcessRefunds', name: 'Reembolsos', description: 'Devolver dinero y anular ventas pagadas.' },
];

const ALL = [...PERMISSION_OPTIONS.map((o) => o.code), 'ManageEmployees'];
const PRESETS = [
  { key: 'manager', name: 'Gerente', description: 'Administra el negocio: equipo, catálogo, reportes y configuración.', appRole: 1, tenantRole: 1, permissions: ALL },
  { key: 'cashier', name: 'Cajero', description: 'Vende, cobra, abre y cierra su caja y atiende el chat.', appRole: 2, tenantRole: 2,
    permissions: ['ManageOrders', 'ChargeOrders', 'ManageCash', 'ManageCustomers', 'ManageWhatsApp', 'ManageAppointments'] },
  { key: 'waiter', name: 'Mesero', description: 'Toma pedidos y cuentas de mesa; no cobra ni ve la caja.', appRole: 2, tenantRole: 2, permissions: ['ManageOrders'] },
  { key: 'kitchen', name: 'Cocina', description: 'Ve las comandas y las marca listas.', appRole: 3, tenantRole: 2, permissions: ['ManageOrders', 'ManageServiceBoard'] },
  { key: 'technician', name: 'Técnico', description: 'Toma los trabajos del taller y los marca terminados.', appRole: 2, tenantRole: 2, permissions: ['ManageServiceBoard', 'ManageAppointments'] },
];

function seedPhase4e(data) {
  if (data.p4e) return;
  const byEmail = (start) => data.employees.find((e) => e.email.startsWith(start));
  byEmail('carlos').permissions = PRESETS[1].permissions;
  byEmail('lucia').permissions = PRESETS[3].permissions;
  byEmail('diego').permissions = ['ManageOrders', 'ChargeOrders', 'ManageCash', 'ManageInventory'];
  byEmail('andrea').permissions = PRESETS[2].permissions;
  data.employees.push({
    tenantUserId: 'tu-sin-acceso', userId: 'user-sin-acceso', email: 'jorge.paz@cafelaesquina.mx', firstName: 'Jorge', lastName: 'Paz',
    phoneNumber: '+5215512340000', appRole: 'Staff', tenantRole: 'Staff', status: 'Active', isActive: true, createdAt: NOW_ISO, permissions: [],
  });
  data.p4e = true;
}

const invitationPath = (userId) => `/aceptar-invitacion?userId=${userId}&token=${encodeURIComponent('CfDJ8demo-token')}`;

export function phase4eRoutes(data, { ok }) {
  seedPhase4e(data);
  const bad = (error) => ({ status: 400, json: { error } });
  const appRoleName = (value) => (value === 1 ? 'TenantAdmin' : value === 3 ? 'Kitchen' : 'Staff');
  return [
    // Staff profiles keep their own role and permissions (the server's current access wins in the client).
    ['GET', /^\/me\/access$/, () => (data.profile.tenantRole === 'Staff'
      ? ok({ userId: data.USER_ID, role: data.profile.role === 'Kitchen' ? 'KITCHEN' : 'STAFF', tenantRole: 'Staff', tenantId: data.TENANT_ID,
        isMember: true, isTenantManager: false, isPlatformAdmin: false, permissions: data.profile.permissions, capabilities: data.tenant.capabilities })
      : null)],
    ['GET', /^\/employees\/role-presets$/, () => ok(PRESETS)],
    ['GET', /^\/employees\/permission-options$/, () => ok(PERMISSION_OPTIONS)],
    ['POST', /^\/employees$/, (m, q, body) => {
      const userId = `user-${data.employees.length + 1}`;
      const employee = {
        tenantUserId: `tu-${data.employees.length + 1}`, userId, email: body.email, firstName: body.firstName, lastName: body.lastName,
        phoneNumber: body.phoneNumber, appRole: appRoleName(body.appRole), tenantRole: body.tenantRole === 1 ? 'Admin' : 'Staff',
        status: 'Invited', isActive: false, createdAt: NOW_ISO, permissions: body.permissions ?? ALL, invitationUrl: invitationPath(userId),
      };
      data.employees.push({ ...employee, invitationUrl: null });
      return ok(employee);
    }],
    ['POST', /^\/employees\/([^/]+)\/resend-invitation$/, (m) => {
      const employee = data.employees.find((e) => e.tenantUserId === m[1]);
      return employee ? ok({ invitationUrl: invitationPath(employee.userId), email: employee.email }) : bad('Empleado no encontrado.');
    }],
    ['PATCH', /^\/employees\/([^/]+)\/active$/, (m, q, body) => {
      const employee = data.employees.find((e) => e.tenantUserId === m[1]);
      if (!employee) return bad('Empleado no encontrado.');
      employee.status = body.isActive ? (employee.status === 'Disabled' ? 'Active' : employee.status) : 'Disabled';
      employee.isActive = employee.status === 'Active';
      return { status: 204, json: null };
    }],
    ['PUT', /^\/employees\/([^/]+)$/, (m, q, body) => {
      const employee = data.employees.find((e) => e.tenantUserId === m[1]);
      if (!employee) return bad('Empleado no encontrado.');
      Object.assign(employee, { firstName: body.firstName, lastName: body.lastName, permissions: body.permissions ?? ALL, appRole: appRoleName(body.appRole) });
      return ok(employee);
    }],
    ['GET', /^\/auth\/invitation$/, (m, q) => (q.get('token') === 'usada'
      ? bad('La invitación ya se usó: entra con tu correo y contraseña.')
      : ok({ email: 'andrea.vazquez@gmail.com', firstName: 'Andrea', businesses: ['Café La Esquina'] }))],
    ['POST', /^\/auth\/accept-invitation$/, () => ok({ accessToken: data.token, expiresAt: '2030-01-01T00:00:00Z', requiresTwoFactor: false })],
    ['POST', /^\/auth\/forgot-password\/sms$/, () => ok({ message: 'Si tu cuenta tiene un teléfono confirmado, te enviamos un código por SMS.' })],
    ['POST', /^\/auth\/reset-password\/sms$/, (m, q, body) => (body.code === '000000'
      ? bad('El código no es válido o ya expiró. Pide uno nuevo.')
      : ok({ message: 'Contraseña restablecida exitosamente.' }))],
    ['POST', /^\/auth\/profile\/phone$/, (m, q, body) => (body.currentPassword === 'mala'
      ? bad('La contraseña no es correcta.')
      : ok({ message: 'Te enviamos un código al número nuevo.' }))],
    ['POST', /^\/auth\/profile\/phone\/confirm$/, (m, q, body) => {
      data.profile.phoneNumber = `+52${body.phoneNumber.replace(/\D/g, '').slice(-10)}`;
      data.profile.phoneNumberConfirmed = true;
      return ok({ message: 'Número de teléfono confirmado exitosamente.' });
    }],
    ['POST', /^\/auth\/change-unconfirmed-phone$/, () => ok({ message: 'Te enviamos un código al número nuevo.' })],
  ];
}
