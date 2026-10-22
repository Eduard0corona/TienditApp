import type { PlatformWithholdingCertificate } from '../../api/platformWithholdings';

// Reportes › Liquidaciones › Retenciones (Fase 22J): how the months and the CFDI read, whatever the browser's locale data.

export const MONTH_NAMES = [
  'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
  'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
] as const;

/** The first year of the CFDI de retenciones of the platforms (clave 26), as the server takes it. */
export const FIRST_WITHHOLDING_YEAR = 2019;

/** The longest XML the server reads, in characters (512 K): a bigger file is not a CFDI de retenciones. */
export const MAX_WITHHOLDING_XML_LENGTH = 512 * 1024;

/** A coarse cap on the file before reading it: those characters in UTF-16, with its mark. */
export const MAX_WITHHOLDING_XML_BYTES = 2 * MAX_WITHHOLDING_XML_LENGTH + 2;

export const XML_TOO_BIG_MESSAGE = 'El archivo pasa de 512 KB: un CFDI de retenciones es mucho más chico. Revisa que sea el XML.';
export const NOT_XML_FILE_MESSAGE = 'No es un archivo .xml: sube el XML del CFDI de retenciones, no el PDF.';
/** A .xml whose bytes are not text, in the server's words. */
export const NOT_XML_CONTENT_MESSAGE = 'El archivo no es un XML válido: sube el XML del CFDI de retenciones, no el PDF.';
/** The browser could not read it: it changed or went away after it was chosen. */
export const FILE_NOT_READABLE_MESSAGE = 'No se pudo leer el archivo: puede que haya cambiado o ya no esté. Vuelve a elegirlo.';

/** «Septiembre»: a month of the table, starting with a capital as the first word of a cell. */
export function monthLabel(month: number): string {
  const name = MONTH_NAMES[month - 1] ?? String(month);
  return name.charAt(0).toUpperCase() + name.slice(1);
}

/** «septiembre de 2026», or «julio a septiembre de 2026» for one of several months, as the server writes it. */
export function describeCertificatePeriod(certificate: Pick<PlatformWithholdingCertificate, 'monthFrom' | 'monthTo' | 'year'>): string {
  const to = MONTH_NAMES[certificate.monthTo - 1];
  return certificate.monthFrom === certificate.monthTo
    ? `${to} de ${certificate.year}`
    : `${MONTH_NAMES[certificate.monthFrom - 1]} a ${to} de ${certificate.year}`;
}

/** The years the screen offers, the newest first: from this one back to 2019. */
export function withholdingYears(currentYear: number): number[] {
  const years: number[] = [];
  for (let year = currentYear; year >= FIRST_WITHHOLDING_YEAR; year -= 1) years.push(year);
  return years;
}

/** «2.50 %»; empty without a rate to show. */
export function formatRate(rate: number | null | undefined): string {
  return rate === null || rate === undefined ? '' : `${rate.toFixed(2)} %`;
}

/** The first characters of a fiscal folio, enough to tell two CFDI apart in a list. */
export function shortUuid(uuid: string): string {
  return uuid.slice(0, 8).toUpperCase();
}
