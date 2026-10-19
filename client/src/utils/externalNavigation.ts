/** Leaves the app for another site (Mercado Pago's checkout or authorization): one place, so tests can watch it. */
export function goToExternalPage(url: string): void {
  window.location.assign(url);
}
