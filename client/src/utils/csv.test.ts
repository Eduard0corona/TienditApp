import { describe, expect, it } from 'vitest';
import { decodeCsv, detectDelimiter, parseCsv, splitHeader } from './csv';

describe('csv', () => {
  it('reads UTF-8 with or without its mark, and Windows-1252 as Excel saves it in Spanish', () => {
    const utf8 = new TextEncoder().encode('﻿Concepto\nCancelación');
    expect(decodeCsv(utf8.buffer)).toBe('Concepto\nCancelación');
    // «Promoción» in Windows-1252: the ó is one byte (0xF3), not valid UTF-8.
    const latin = new Uint8Array([0x50, 0x72, 0x6f, 0x6d, 0x6f, 0x63, 0x69, 0xf3, 0x6e]);
    expect(decodeCsv(latin.buffer)).toBe('Promoción');
  });

  it('reads UTF-16 with its mark, and refuses a file that is not text', () => {
    const text = '\ufeffFolio;Importe';
    const utf16 = new Uint8Array(text.length * 2);
    for (let index = 0; index < text.length; index += 1) {
      utf16[index * 2] = text.charCodeAt(index) & 0xff;
      utf16[index * 2 + 1] = text.charCodeAt(index) >> 8;
    }
    expect(decodeCsv(utf16.buffer)).toBe('Folio;Importe');
    // An Excel workbook is a ZIP («PK»); a PDF or any binary has NUL bytes.
    expect(decodeCsv(new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0x14]).buffer)).toBeNull();
    expect(decodeCsv(new Uint8Array([0x25, 0x50, 0x44, 0x46, 0x00, 0x01]).buffer)).toBeNull();
  });

  it('finds the separator that splits the first lines the same way, outside quotes', () => {
    expect(detectDelimiter('folio,importe\n1,2')).toBe(',');
    expect(detectDelimiter('\n\nfolio;importe;concepto\n1;2,50;x')).toBe(';');
    expect(detectDelimiter('folio\timporte')).toBe('\t');
    expect(detectDelimiter('"folio;con punto y coma",importe')).toBe(',');
    // A title above the header, decimal commas without quotes and lines that end in CR only.
    expect(detectDelimiter('Estado de cuenta, semana 41\rFolio;Importe;Concepto\r1;12,50;Pedido\r2;7,00;Pedido\r')).toBe(';');
  });

  it('takes as header the first row with two or more columns, after any title', () => {
    const rows = parseCsv('Estado de cuenta Rappi\n\nFolio,Importe\n152390,561.48\n');
    expect(splitHeader(rows)).toEqual({ header: ['Folio', 'Importe'], rows: [{ line: 4, cells: ['152390', '561.48'] }] });
    expect(splitHeader(parseCsv('Folio,Importe\n'))).toBeNull();
    expect(splitHeader(parseCsv('solo un título\n'))).toBeNull();
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
