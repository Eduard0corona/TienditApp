// The words of the page for businesses (/negocios). Everything here is something TienditApp does today on master: a
// feature that is not built yet (loyalty points, an API with the delivery apps, AI in the chat) stays out until it is.

/** Where every call to action of the page goes: the registration, already on the business account. */
export const SIGN_UP_PATH = '/register?cuenta=negocio';

/** The title of the tab: the same one the server writes for search engines (PublicPages.BusinessesTitle). */
export const LANDING_TITLE = 'TienditApp para negocios: punto de venta, menú en línea y facturación';

export interface IconItem {
  icon: string;
  title: string;
  text: string;
}

export const NAV_LINKS = [
  { href: '#funciones', label: 'Funciones' },
  { href: '#giros', label: 'Giros' },
  { href: '#como-empezar', label: 'Cómo empezar' },
  { href: '#planes', label: 'Planes' },
  { href: '#preguntas', label: 'Preguntas' },
] as const;

/** The business types of the registration, as a visitor would name theirs. */
export const INDUSTRIES = [
  { icon: 'ri-restaurant-2-line', label: 'Restaurantes y fondas' },
  { icon: 'ri-cup-line', label: 'Cafeterías' },
  { icon: 'ri-e-bike-2-line', label: 'Cocinas a domicilio' },
  { icon: 'ri-shopping-basket-2-line', label: 'Abarrotes' },
  { icon: 'ri-capsule-line', label: 'Farmacias' },
  { icon: 'ri-t-shirt-line', label: 'Ropa y accesorios' },
  { icon: 'ri-smartphone-line', label: 'Electrónica' },
  { icon: 'ri-hammer-line', label: 'Ferreterías' },
  { icon: 'ri-pencil-ruler-2-line', label: 'Papelerías' },
  { icon: 'ri-scissors-cut-line', label: 'Estéticas y barberías' },
  { icon: 'ri-car-line', label: 'Talleres' },
  { icon: 'ri-stethoscope-line', label: 'Consultorios' },
  { icon: 'ri-run-line', label: 'Gimnasios' },
  { icon: 'ri-shirt-line', label: 'Lavanderías' },
] as const;

export const PILLARS: IconItem[] = [
  {
    icon: 'ri-apps-2-line',
    title: 'Todo en un solo lugar',
    text: 'Punto de venta, tienda en línea, inventario, caja y facturación que se hablan entre sí. Una venta descuenta su receta, entra al corte y queda lista para facturar.',
  },
  {
    icon: 'ri-map-pin-2-line',
    title: 'Pensado para México',
    text: 'Precios en pesos, IVA de 16, 8 y 0 %, CFDI 4.0, cobros con Mercado Pago, pedidos de Rappi, Uber Eats y DiDi Food, y avisos por WhatsApp.',
  },
  {
    icon: 'ri-community-line',
    title: 'Crece con tu colonia',
    text: 'Tu negocio aparece en Explorar, donde los clientes de tu ciudad buscan dónde comprar, pedir a domicilio o agendar su cita.',
  },
];

/** How an order travels through TienditApp, from the channel it came in to the report. */
export const FLOW_STEPS: IconItem[] = [
  { icon: 'ri-store-2-line', title: 'Entra la venta', text: 'En mostrador, en una mesa, por tu menú en línea, por WhatsApp o desde Rappi, Uber Eats y DiDi Food.' },
  { icon: 'ri-fire-line', title: 'Se prepara', text: 'Llega a la pantalla de cocina o al tablero del taller, y quien pidió en línea ve cómo va su pedido.' },
  { icon: 'ri-archive-stack-line', title: 'Descuenta inventario', text: 'Cada venta resta sus insumos según la receta y te avisa antes de que algo se acabe.' },
  { icon: 'ri-safe-2-line', title: 'Cuadra la caja', text: 'Efectivo, tarjeta, transferencia, en línea o fiado: todo entra al corte de cada cajero.' },
  { icon: 'ri-file-text-line', title: 'Se factura', text: 'Facturas el pedido, haces la global del día o el cliente se factura solo con el QR de su ticket.' },
  { icon: 'ri-bar-chart-box-line', title: 'Lo ves en tus reportes', text: 'Ventas por sucursal y por canal, ticket promedio, utilidad y lo que te debe cada plataforma.' },
];

export interface BusinessTypeTab {
  id: string;
  icon: string;
  label: string;
  /** Who it is for, in the words of the registration. */
  forWho: string;
  features: { title: string; text: string }[];
  /** A day of that business: what moves on its board, with the state chip of the app. */
  boardTitle: string;
  board: { icon: string; text: string; state: string; tone: 'new' | 'preparing' | 'ready' | 'confirmed' }[];
}

export const BUSINESS_TYPES: BusinessTypeTab[] = [
  {
    id: 'comida',
    icon: 'ri-restaurant-2-line',
    label: 'Comida',
    forWho: 'Restaurantes, fondas, taquerías, cafeterías y cocinas que solo entregan a domicilio.',
    features: [
      { title: 'Mesas y cuentas abiertas', text: 'por mesa o a nombre de alguien, con rondas que van directo a cocina y varias formas de pago en una cuenta.' },
      { title: 'Pantalla de cocina', text: 'con las comandas en vivo y sonido cuando entra un pedido.' },
      { title: 'Menú en línea con QR', text: 'para recoger o a domicilio, con un QR por mesa que dice de dónde viene el pedido.' },
      { title: 'Recetas e insumos', text: 'cada platillo descuenta lo que lleva; si cancelas uno, sus insumos regresan.' },
      { title: 'Apps de reparto', text: 'registra los pedidos de Rappi, Uber Eats y DiDi Food con su folio y sus propios precios, y concilia sus depósitos.' },
    ],
    boardTitle: 'Así se ve la hora de la comida',
    board: [
      { icon: 'ri-qr-code-line', text: 'Mesa 4 · 2 tacos al pastor', state: 'Nuevo', tone: 'new' },
      { icon: 'ri-fire-line', text: 'Para llevar · Torta de milanesa', state: 'En cocina', tone: 'preparing' },
      { icon: 'ri-e-bike-2-line', text: 'A domicilio · Pozole grande', state: 'Listo', tone: 'ready' },
      { icon: 'ri-smartphone-line', text: 'DiDi Food · Orden de quesadillas', state: 'Confirmado', tone: 'confirmed' },
    ],
  },
  {
    id: 'comercio',
    icon: 'ri-store-2-line',
    label: 'Comercio',
    forWho: 'Abarrotes, farmacias, papelerías, ferreterías, tiendas de ropa y de electrónica.',
    features: [
      { title: 'Cobro con código de barras', text: 'busca o escanea, vende a granel por kilo, litro o metro, y cobra con varias formas de pago a la vez.' },
      { title: 'Fiado con límite', text: 'cada cliente con su límite y su plazo, abonos, recordatorios por WhatsApp y saldo a favor.' },
      { title: 'Inventario que cuadra', text: 'existencias por sucursal, conteo físico, transferencias entre ubicaciones y alertas de lo que se acaba.' },
      { title: 'Compras a proveedores', text: 'órdenes de compra, recepciones parciales y el pedido enviado al proveedor por WhatsApp.' },
      { title: 'Tu tienda en línea', text: 'tu catálogo en un enlace, con envío por código postal, pedido mínimo y envío gratis desde el monto que decidas.' },
    ],
    boardTitle: 'Así se ve un día en la tienda',
    board: [
      { icon: 'ri-barcode-line', text: 'Mostrador · 6 productos', state: 'Cobrado', tone: 'ready' },
      { icon: 'ri-hand-coin-line', text: 'Fiado · Doña Lupe abonó $200', state: 'Al corriente', tone: 'confirmed' },
      { icon: 'ri-alarm-warning-line', text: 'Leche entera · quedan 4', state: 'Por surtir', tone: 'new' },
      { icon: 'ri-e-bike-2-line', text: 'Pedido en línea · CP 72000', state: 'En camino', tone: 'preparing' },
    ],
  },
  {
    id: 'servicios',
    icon: 'ri-tools-line',
    label: 'Servicios',
    forWho: 'Estéticas, barberías, talleres, lavanderías, consultorios, gimnasios y servicios a domicilio.',
    features: [
      { title: 'Agenda sin traslapes', text: 'cada cita con su servicio y la persona que atiende, con recordatorios por WhatsApp.' },
      { title: 'Citas en línea', text: 'el cliente elige servicio, con quién, día y hora libre; tú la confirmas.' },
      { title: 'Taller con fotos', text: 'órdenes de servicio con piezas y fotos del antes y el después tomadas con el teléfono.' },
      { title: 'Visitas a domicilio', text: 'cargo por visita según la zona, tiempo de traslado entre citas y la ruta del día para abrir en el mapa.' },
      { title: 'Vende también productos', text: 'el champú, la refacción o el detergente salen del mismo inventario y del mismo corte.' },
    ],
    boardTitle: 'Así se ve tu agenda',
    board: [
      { icon: 'ri-calendar-event-line', text: '10:00 · Corte y barba con Luis', state: 'Por confirmar', tone: 'new' },
      { icon: 'ri-tools-line', text: 'Orden 128 · Afinación', state: 'En proceso', tone: 'preparing' },
      { icon: 'ri-camera-line', text: 'Orden 127 · Fotos de entrega', state: 'Listo', tone: 'ready' },
      { icon: 'ri-home-4-line', text: '16:30 · Visita a domicilio', state: 'Confirmada', tone: 'confirmed' },
    ],
  },
];

export interface FeatureGroup {
  id: string;
  title: string;
  text: string;
  items: IconItem[];
}

export const FEATURE_GROUPS: FeatureGroup[] = [
  {
    id: 'vende',
    title: 'Vende en todos lados',
    text: 'En mostrador, en la mesa, en línea y en las apps de reparto, con una sola lista de productos y precios.',
    items: [
      { icon: 'ri-shopping-cart-2-line', title: 'Punto de venta', text: 'Cobra rápido con variantes y extras, propina, IVA por producto y ticket impreso o por WhatsApp.' },
      { icon: 'ri-qr-code-line', title: 'Menú y tienda en línea', text: 'Tu catálogo en un enlace y un QR para mesas, redes y WhatsApp, con portada y destacados como las apps de entrega.' },
      { icon: 'ri-e-bike-2-line', title: 'Pedidos a domicilio', text: 'Zonas por código postal con su costo de envío, cobro contra entrega y seguimiento paso a paso para tu cliente.' },
      { icon: 'ri-secure-payment-line', title: 'Pagos en línea', text: 'Tu cliente paga con Mercado Pago y el dinero llega a tu cuenta. TienditApp no cobra nada extra ni ve las tarjetas.' },
      { icon: 'ri-smartphone-line', title: 'Rappi, Uber Eats y DiDi Food', text: 'Registra sus pedidos con su folio y su precio en cada app; TienditApp calcula comisión, retenciones y lo que te deben depositar.' },
      { icon: 'ri-wifi-off-line', title: 'Sin internet, sigues cobrando', text: 'Si se va la conexión, el mostrador sigue vendiendo y manda las ventas en cuanto regresa.' },
    ],
  },
  {
    id: 'opera',
    title: 'Opera sin papelitos',
    text: 'Cada pedido, cita y orden de servicio sabe en qué va, y cada insumo sabe cuánto queda.',
    items: [
      { icon: 'ri-fire-line', title: 'Cocina', text: 'Comandas en vivo con sonido, rondas por mesa y platillos cancelados con su motivo.' },
      { icon: 'ri-tools-line', title: 'Taller', text: 'Órdenes de servicio con piezas, un tablero por etapa y fotos del antes y el después.' },
      { icon: 'ri-calendar-check-line', title: 'Agenda y citas en línea', text: 'Citas sin traslapes por servicio y por persona, a domicilio o en tu local, con recordatorios.' },
      { icon: 'ri-archive-stack-line', title: 'Inventario y recetas', text: 'Insumos, recetas, historial por insumo, conteo físico, transferencias y alertas de existencias.' },
      { icon: 'ri-truck-line', title: 'Compras y proveedores', text: 'Órdenes de compra, recepciones parciales y el costo real de lo que compraste.' },
    ],
  },
  {
    id: 'controla',
    title: 'Controla tu dinero',
    text: 'Sabes cuánto vendiste, cuánto hay en caja, quién te debe y cuánto te van a depositar.',
    items: [
      { icon: 'ri-safe-2-line', title: 'Caja y cortes', text: 'Una caja por cajero, turnos, corte del día impreso o por WhatsApp y liquidación de repartidores.' },
      { icon: 'ri-file-text-line', title: 'Facturación CFDI 4.0', text: 'Factura del pedido, factura global, parcialidades con complemento de pago, notas de crédito y autofactura con el QR del ticket.' },
      { icon: 'ri-hand-coin-line', title: 'Fiado y saldo a favor', text: 'Cuentas por cobrar con límite y plazo por cliente, abonos, recordatorios y anticipos que se usan como pago.' },
      { icon: 'ri-bar-chart-box-line', title: 'Reportes', text: 'Ventas por sucursal y por canal, ticket promedio, utilidad bruta y lo más vendido.' },
      { icon: 'ri-building-2-line', title: 'Sucursales', text: 'Cada una con su caja, su inventario y su ubicación, abierta al público o solo a domicilio.' },
      { icon: 'ri-team-line', title: 'Equipo y permisos', text: 'Dueño, gerente, cajero, mesero, cocina o técnico, cada quien con lo suyo. Los invitas con un enlace.' },
    ],
  },
  {
    id: 'conecta',
    title: 'Conecta con tus clientes',
    text: 'Que te encuentren, que regresen y que sepan de ti sin que tengas que estar al teléfono.',
    items: [
      { icon: 'ri-compass-3-line', title: 'Explorar', text: 'Tu negocio en el directorio de TienditApp, con búsqueda por producto, «Cerca de mí» y favoritos.' },
      { icon: 'ri-star-smile-line', title: 'Reseñas', text: 'Tus clientes te califican y tú respondes cada reseña desde tu negocio.' },
      { icon: 'ri-whatsapp-line', title: 'Mensajes de WhatsApp', text: 'Chat en vivo con tus clientes y avisos de su pedido, de su cita o de lo que deben.' },
      { icon: 'ri-chat-check-line', title: 'Respuestas automáticas', text: 'Contesta solo el horario, si llegas a su código postal, el costo de envío y el enlace a tu menú.' },
      { icon: 'ri-megaphone-line', title: 'Campañas', text: 'Mensajes a todos tus clientes, a los frecuentes o a los que dejaron de venir.' },
      { icon: 'ri-contacts-book-2-line', title: 'Clientes', text: 'La ficha de cada cliente con sus pedidos, direcciones, notas, chat y datos para facturar.' },
    ],
  },
];

export const BENEFITS: IconItem[] = [
  { icon: 'ri-line-chart-line', title: 'Vende más', text: 'Abres canales nuevos sin contratar a nadie: tu tienda en línea, tu QR y Explorar trabajan aunque estés atendiendo.' },
  { icon: 'ri-percent-line', title: 'Sin comisión por pedido', text: 'Pagas tu plan, no un porcentaje de cada venta en línea. Los pagos con Mercado Pago llegan directo a tu cuenta.' },
  { icon: 'ri-time-line', title: 'Ahorra horas cada semana', text: 'El corte, el inventario, los recordatorios y la factura global salen de lo que ya vendiste, sin capturar dos veces.' },
  { icon: 'ri-scales-3-line', title: 'Cuentas claras', text: 'Cada movimiento queda registrado: caja por cajero, historial por insumo y bitácora de lo que hace tu equipo.' },
  { icon: 'ri-government-line', title: 'Al día con el SAT', text: 'Facturas CFDI 4.0 con tu propio certificado y tus clientes se facturan solos desde el ticket.' },
  { icon: 'ri-device-line', title: 'Sin instalar nada', text: 'Funciona en el navegador del celular, la tablet o la computadora que ya tienes.' },
  { icon: 'ri-palette-line', title: 'Con tu marca', text: 'Tu logo y uno de ocho temas de color, como Talavera o Jacaranda, en tu equipo y en tu menú.' },
  { icon: 'ri-shield-check-line', title: 'Seguro', text: 'Verificación en dos pasos, permisos por persona y tus datos separados de los de cualquier otro negocio.' },
];

export const BEFORE = [
  'Pedidos en libretas, papelitos y chats de WhatsApp',
  'El corte cuadra… o no, y no sabes por qué',
  'Te enteras de que se acabó algo cuando te lo piden',
  'El fiado vive en un cuaderno',
  'Facturar es ir al portal del SAT al final del día',
  'Si no te conocen, no te encuentran',
];

export const AFTER = [
  'Todos los pedidos en un solo tablero, con su estado',
  'Un corte por cajero que suma cada forma de pago',
  'Alertas de existencias antes de que se acaben',
  'Cada cliente con su límite, su plazo y sus abonos',
  'La factura sale del pedido, o tu cliente se la hace solo',
  'Tu negocio en Explorar, con tu menú y tus reseñas',
];

export const STEPS = [
  { title: 'Crea tu cuenta', text: 'Tus datos, el nombre de tu negocio y su tipo. Los datos fiscales pueden esperar a que quieras facturar.' },
  { title: 'Sigue los primeros pasos', text: 'Cuatro pasos para tu primera venta: tu negocio, precios e IVA, y tu catálogo, que puedes empezar con un ejemplo de tu giro.' },
  { title: 'Abre caja y comparte tu QR', text: 'Invita a tu equipo, publica tu menú en línea y pon tu QR en el mostrador, las mesas y tus redes.' },
  { title: 'Vende y crece', text: 'Enciende cocina, agenda, inventario, facturación o más sucursales cuando los necesites.' },
];

export const PLAN_INCLUDES = [
  'Punto de venta, caja y cortes',
  'Menú y tienda en línea con QR',
  'Pedidos de Rappi, Uber Eats y DiDi Food en todos los planes',
  'Tu negocio en Explorar',
  'Actualizaciones sin costo y sin instalar nada',
];

export const FAQ = [
  {
    question: '¿Cuánto cuesta TienditApp?',
    answer: 'Crear tu cuenta es gratis y no pide tarjeta. Después eliges un plan mensual en pesos según los módulos, las sucursales y las personas de tu equipo. No cobramos comisión por tus ventas.',
  },
  {
    question: '¿Necesito comprar equipo o instalar algo?',
    answer: 'No. TienditApp funciona en el navegador de tu celular, tablet o computadora. Si ya tienes una impresora de tickets o un lector de código de barras, los puedes usar.',
  },
  {
    question: '¿Qué pasa si se va el internet?',
    answer: 'El mostrador sigue cobrando sin conexión y envía las ventas en cuanto regresa. El fiado y el pago con saldo a favor sí necesitan conexión.',
  },
  {
    question: '¿Puedo facturar?',
    answer: 'Sí. Con tu certificado de sello digital facturas CFDI 4.0: la factura de un pedido, la global a público en general, parcialidades con complemento de pago y notas de crédito. Tus clientes también se pueden facturar solos con el QR de su ticket.',
  },
  {
    question: '¿Se conecta con Rappi, Uber Eats y DiDi Food?',
    answer: 'Registras sus pedidos en TienditApp con el folio de la app y TienditApp calcula la comisión, las retenciones y lo que te deben depositar. Después concilias cada depósito o importas su estado de cuenta. Los pedidos no llegan solos desde esas apps.',
  },
  {
    question: '¿Cómo cobro los pedidos en línea?',
    answer: 'Al recibirlos o en línea con tu cuenta de Mercado Pago. El dinero llega directo a ti: TienditApp no cobra nada extra ni ve las tarjetas.',
  },
  {
    question: '¿Puedo usar WhatsApp con mis clientes?',
    answer: 'Sí, conectando la cuenta de WhatsApp Business de tu negocio (la API de Meta). Desde ahí chateas, mandas avisos, recordatorios y campañas. Meta cobra sus plantillas de mensaje aparte.',
  },
  {
    question: '¿Sirve si tengo varias sucursales?',
    answer: 'Sí. Cada sucursal tiene su caja, su inventario y su ubicación, y puedes ver tus ventas por sucursal. También puedes tener una cocina que solo entrega a domicilio.',
  },
  {
    question: '¿Mi información está segura?',
    answer: 'Los datos de tu negocio están separados de los de cualquier otro. Puedes activar la verificación en dos pasos, dar a cada persona solo los permisos que necesita y revisar la bitácora de actividad.',
  },
  {
    question: '¿Puedo dejar de aparecer en Explorar?',
    answer: 'Sí. Apareces cuando publicas tu menú en línea, y lo apagas con un interruptor en Configuración. Tu enlace y tu QR siguen funcionando.',
  },
];
