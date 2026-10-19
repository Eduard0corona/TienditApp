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
