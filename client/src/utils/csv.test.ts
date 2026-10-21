import { describe, expect, it } from 'vitest';
import { decodeCsv, detectDelimiter, parseCsv } from './csv';

describe('csv', () => {
  it('reads UTF-8 with or without its mark, and Windows-1252 as Excel saves it in Spanish', () => {
    const utf8 = new TextEncoder().encode('﻿Concepto\nCancelación');
    expect(decodeCsv(utf8.buffer)).toBe('Concepto\nCancelación');
    // «Promoción» in Windows-1252: the ó is one byte (0xF3), not valid UTF-8.
    const latin = new Uint8Array([0x50, 0x72, 0x6f, 0x6d, 0x6f, 0x63, 0x69, 0xf3, 0x6e]);
    expect(decodeCsv(latin.buffer)).toBe('Promoción');
  });

  it('finds the separator of the first line, outside quotes', () => {
    expect(detectDelimiter('folio,importe\n1,2')).toBe(',');
    expect(detectDelimiter('\n\nfolio;importe;concepto\n1;2,50;x')).toBe(';');
    expect(detectDelimiter('folio\timporte')).toBe('\t');
    expect(detectDelimiter('"folio;con punto y coma",importe')).toBe(',');
  });

  it('keeps separators, line breaks and quotes inside quotes, and the line of each row', () => {
    const text = 'folio,concepto,importe\r\n152390,"Pedido, con ""extra""",561.48\r\n\r\n152001,"Dos\nlíneas",-20\r\n';

    expect(parseCsv(text)).toEqual([
      { line: 1, cells: ['folio', 'concepto', 'importe'] },
      { line: 2, cells: ['152390', 'Pedido, con "extra"', '561.48'] },
      { line: 4, cells: ['152001', 'Dos\nlíneas', '-20'] },
    ]);
    expect(parseCsv('a;b\n1;2,5', ';')).toEqual([{ line: 1, cells: ['a', 'b'] }, { line: 2, cells: ['1', '2,5'] }]);
  });
});
