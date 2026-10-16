import { asOption, type DemoData } from '../fixtures';

// OnlineMenuMode as the contract numbers it.
const ORDERS = 2;

/** No business published its menu yet. */
function emptyDirectory(data: DemoData) {
  data.directory = [];
}

/** The demo business takes orders online, so Explorar lists it. */
function menuWithOrders(data: DemoData) {
  data.tenant.onlineMenuMode = ORDERS;
}

export const SIN_NEGOCIOS = asOption(emptyDirectory);
export const MENU_CON_PEDIDOS = asOption(menuWithOrders);
