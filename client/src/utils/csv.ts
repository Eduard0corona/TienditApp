// A CSV file read in the browser (Fase 22H: the statements of the delivery platforms). Nothing is uploaded: the screen
// reads the file and sends the lines it chose to the API, which decides.

/** The largest file read: a month of a busy kitchen fits many times. */
export const MAX_CSV_BYTES = 5 * 1024 * 1024;

export const CSV_TOO_BIG_MESSAGE = 'El archivo pesa más de 5 MB. Descarga solo el periodo del depósito.';
export const CSV_EMPTY_MESSAGE = 'El archivo no tiene renglones.';

const DELIMITERS = [',', ';', '\t'] as const;
export type CsvDelimiter = (typeof DELIMITERS)[number];

/**
 * The text of a file: UTF-8 (with or without its mark) and, when it is not valid UTF-8, Windows-1252, as Excel saves a
 * CSV in Spanish.
 */
export function decodeCsv(bytes: ArrayBuffer): string {
  let text: string;
  try {
    text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    text = new TextDecoder('windows-1252').decode(bytes);
  }
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

/** The separator of the first line that has one, outside quotes: comma, semicolon (Excel in Spanish) or tab. */
export function detectDelimiter(text: string): CsvDelimiter {
  const firstLine = text.split(/\r?\n/).find((line) => line.trim() !== '') ?? '';
  const counts = new Map<CsvDelimiter, number>(DELIMITERS.map((delimiter) => [delimiter, 0]));
  let quoted = false;
  for (const char of firstLine) {
    if (char === '"') quoted = !quoted;
    else if (!quoted && counts.has(char as CsvDelimiter)) counts.set(char as CsvDelimiter, (counts.get(char as CsvDelimiter) ?? 0) + 1);
  }
  let best: CsvDelimiter = ',';
  for (const delimiter of DELIMITERS) {
    if ((counts.get(delimiter) ?? 0) > (counts.get(best) ?? 0)) best = delimiter;
  }
  return best;
}

/**
 * The rows of a CSV (RFC 4180): a field in quotes may hold the separator, a line break or a quote written twice. Each row
 * keeps its line in the file (from 1), so the person finds it; empty lines are left out.
 */
export function parseCsv(text: string, delimiter: CsvDelimiter = detectDelimiter(text)): { line: number; cells: string[] }[] {
  const rows: { line: number; cells: string[] }[] = [];
  let cells: string[] = [];
  let cell = '';
  let quoted = false;
  let line = 1;
  let rowLine = 1;
  const endRow = () => {
    cells.push(cell);
    if (cells.some((value) => value.trim() !== '')) rows.push({ line: rowLine, cells });
    cells = [];
    cell = '';
  };

  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (quoted) {
      if (char === '"') {
        if (text[index + 1] === '"') {
          cell += '"';
          index += 1;
        } else {
          quoted = false;
        }
      } else {
        if (char === '\n') line += 1;
        cell += char;
      }
      continue;
    }
    if (char === '"' && cell.trim() === '') {
      cell = '';
      quoted = true;
    } else if (char === delimiter) {
      cells.push(cell);
      cell = '';
    } else if (char === '\r' && text[index + 1] === '\n') {
      // The \n that follows ends the row.
    } else if (char === '\n' || char === '\r') {
      endRow();
      line += 1;
      rowLine = line;
    } else {
      cell += char;
    }
  }
  endRow();
  return rows;
}
