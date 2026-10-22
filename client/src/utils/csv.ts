// A CSV file read in the browser (Fase 22H: the statements of the delivery platforms). Nothing is uploaded: the screen
// reads the file and sends the lines it chose to the API, which decides.

/** The largest file read: a month of a busy kitchen fits many times. */
export const MAX_CSV_BYTES = 5 * 1024 * 1024;

export const CSV_TOO_BIG_MESSAGE = 'El archivo pesa más de 5 MB. Descarga solo el periodo del depósito.';
export const CSV_EMPTY_MESSAGE = 'El archivo no tiene renglones.';
export const CSV_NOT_TEXT_MESSAGE = 'El archivo no es un CSV. Ábrelo en Excel y guárdalo con Guardar como › CSV.';

const DELIMITERS = [',', ';', '\t'] as const;
export type CsvDelimiter = (typeof DELIMITERS)[number];

/** The lines the separator is guessed from: a title above the header does not decide it. */
const SAMPLED_LINES = 20;

/**
 * The text of a file: UTF-8 (with or without its mark), UTF-16 with its mark (Excel's «Texto Unicode») and, when it is
 * not valid UTF-8, Windows-1252, as Excel saves a CSV in Spanish. Null for a file that is not text: an Excel workbook or
 * a PDF (a ZIP or a NUL byte, which no CSV has).
 */
export function decodeCsv(buffer: ArrayBuffer): string | null {
  const bytes = new Uint8Array(buffer);
  let text: string;
  if (bytes[0] === 0xff && bytes[1] === 0xfe) text = new TextDecoder('utf-16le').decode(bytes.subarray(2));
  else if (bytes[0] === 0xfe && bytes[1] === 0xff) text = new TextDecoder('utf-16be').decode(bytes.subarray(2));
  else {
    if ((bytes[0] === 0x50 && bytes[1] === 0x4b) || bytes.subarray(0, 4096).includes(0)) return null;
    try {
      text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
    } catch {
      text = new TextDecoder('windows-1252').decode(bytes);
    }
  }
  if (text.includes('\0')) return null;
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

/** How many times each separator appears in a line, outside quotes. */
function countOutsideQuotes(line: string): Map<CsvDelimiter, number> {
  const counts = new Map<CsvDelimiter, number>(DELIMITERS.map((delimiter) => [delimiter, 0]));
  let quoted = false;
  for (const char of line) {
    if (char === '"') quoted = !quoted;
    else if (!quoted && counts.has(char as CsvDelimiter)) counts.set(char as CsvDelimiter, (counts.get(char as CsvDelimiter) ?? 0) + 1);
  }
  return counts;
}

/**
 * The separator of the file: comma, semicolon (Excel in Spanish) or tab, whichever splits the most of its first lines into
 * the same number of columns. A title above the header, or a decimal comma in a file separated by semicolons, does not
 * mislead it.
 */
export function detectDelimiter(text: string): CsvDelimiter {
  const lines = text.slice(0, 64 * 1024).split(/\r\n|\r|\n/).filter((line) => line.trim() !== '').slice(0, SAMPLED_LINES);
  const perLine = lines.map(countOutsideQuotes);
  let best: CsvDelimiter = ',';
  let bestScore = [0, 0];
  for (const delimiter of DELIMITERS) {
    // The most common count of the separator in a line (when it appears), and in how many lines.
    const tally = new Map<number, number>();
    for (const counts of perLine) {
      const count = counts.get(delimiter) ?? 0;
      if (count > 0) tally.set(count, (tally.get(count) ?? 0) + 1);
    }
    for (const [count, lineCount] of tally) {
      if (lineCount > bestScore[0] || (lineCount === bestScore[0] && count > bestScore[1])) {
        best = delimiter;
        bestScore = [lineCount, count];
      }
    }
  }
  return best;
}

/** The header of a file and the rows after it: the first row with two or more columns, after any title. */
export function splitHeader(rows: { line: number; cells: string[] }[]): { header: string[]; rows: { line: number; cells: string[] }[] } | null {
  const at = rows.findIndex((row) => row.cells.filter((cell) => cell.trim() !== '').length >= 2);
  if (at < 0 || at === rows.length - 1) return null;
  return { header: rows[at].cells.map((cell) => cell.trim()), rows: rows.slice(at + 1) };
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
