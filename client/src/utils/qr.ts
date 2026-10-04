// QR codes drawn in the browser (qrcode-generator), so the menu link never goes to a third party.
import qrcode from 'qrcode-generator';

function build(text: string) {
  const qr = qrcode(0, 'M');
  qr.addData(text);
  qr.make();
  return qr;
}

/** Scalable SVG for the screen, as a data URL for an img. */
export function createQrSvgDataUrl(text: string): string {
  const svg = build(text).createSvgTag({ cellSize: 4, margin: 4, scalable: true });
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
}

/** PNG to download and print (about 1000 px wide). */
export function createQrPngDataUrl(text: string): string {
  const qr = build(text);
  const cellSize = Math.max(4, Math.floor(1000 / (qr.getModuleCount() + 8)));
  const size = (qr.getModuleCount() + 8) * cellSize;
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const context = canvas.getContext('2d');
  if (!context) return createQrSvgDataUrl(text);
  context.fillStyle = '#ffffff';
  context.fillRect(0, 0, size, size);
  context.translate(4 * cellSize, 4 * cellSize);
  qr.renderTo2dContext(context, cellSize);
  return canvas.toDataURL('image/png');
}

/** Public address of a business menu, optionally for one table. */
export function buildMenuUrl(slug: string, table?: string): string {
  const url = new URL(`/m/${encodeURIComponent(slug)}`, window.location.origin);
  if (table) url.searchParams.set('mesa', table);
  return url.toString();
}
