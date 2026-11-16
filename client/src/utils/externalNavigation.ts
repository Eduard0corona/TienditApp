/**
 * Leaves the app for another site (Mercado Pago's checkout or authorization): one place, so tests can watch it. Only to
 * an https page, like the server checks: anything else (`javascript:`, plain http) is never followed.
 */
export function goToExternalPage(url: string): void {
  if (!isSecurePage(url)) throw new Error('La página de pago no es segura.');
  window.location.assign(url);
}

function isSecurePage(url: string): boolean {
  try {
    return new URL(url).protocol === 'https:';
  } catch {
    return false;
  }
}

/**
 * Goes back to the AI assistant that asked for the customer's permission (Fase 32E), with its code or its error: an https
 * page, or a desktop app on this device (http to localhost), as the server checked the assistant registered.
 */
export function returnToAssistant(url: string): void {
  if (!isAssistantPage(url)) throw new Error('La dirección del asistente no es segura.');
  window.location.assign(url);
}

function isAssistantPage(url: string): boolean {
  try {
    const page = new URL(url);
    return page.protocol === 'https:'
      || (page.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(page.hostname));
  } catch {
    return false;
  }
}
