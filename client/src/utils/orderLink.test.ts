import { describe, expect, it } from 'vitest';
import { orderLinkMessage, orderLinkOf } from './orderLink';

const LATTE = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
const GRANDE = '9b2d7f1e-1c3a-4f5b-8d6e-7a8b9c0d1e2f';
const AVENA = '0c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f';
const CONCHA = '7d8e9f0a-1b2c-4d3e-9f4a-5b6c7d8e9f0a';

describe('orderLinkOf (Fase 32A)', () => {
  it('has no cart without the parameter', () => {
    expect(orderLinkOf([])).toBeNull();
  });

  it('reads each product with its quantity and options, and the same one twice as one line', () => {
    expect(orderLinkOf([`${LATTE}:2:${GRANDE}:${AVENA}`, `${CONCHA}:1`, `${LATTE}:1:${AVENA}:${GRANDE}`, ` ${CONCHA} `])).toEqual({
      lines: [
        // The same options in another order are the same line, with the options as its last one wrote them.
        { productId: LATTE, optionIds: [AVENA, GRANDE], quantity: 3 },
        { productId: CONCHA, optionIds: [], quantity: 2 },
      ],
      invalid: 0,
    });
  });

  it('counts what is not a product: a bad id, a quantity that is not a whole number from 1, a bad option', () => {
    const link = orderLinkOf([`${LATTE}:0`, `${LATTE}:1.5`, `${LATTE}:dos`, `${LATTE}:-1`, 'no es un id:1', `${LATTE}:1:<script>`, `${CONCHA}:1`, '']);
    expect(link).toEqual({ lines: [{ productId: CONCHA, optionIds: [], quantity: 1 }], invalid: 7 });
  });

  it('takes 1 without a quantity and at most what a line takes', () => {
    expect(orderLinkOf([LATTE, `${CONCHA}:500`])?.lines).toEqual([
      { productId: LATTE, optionIds: [], quantity: 1 },
      { productId: CONCHA, optionIds: [], quantity: 99 },
    ]);
  });

  it('takes at most the lines a cart takes, and counts the rest', () => {
    const values = Array.from({ length: 52 }, (_, index) => `p${index}:1`);
    const link = orderLinkOf(values);
    expect(link?.lines).toHaveLength(50);
    expect(link?.invalid).toBe(2);
  });
});

describe('orderLinkMessage (Fase 32A)', () => {
  it('says nothing when everything went in the cart', () => {
    expect(orderLinkMessage(3, 0)).toBe('');
  });

  it('says what could not go in, in singular and plural, and when nothing did', () => {
    expect(orderLinkMessage(2, 1)).toBe('Agregamos lo que sigue disponible; 1 producto del enlace no se pudo agregar: ya no está en el menú o le falta elegir una opción.');
    expect(orderLinkMessage(1, 3)).toBe('Agregamos lo que sigue disponible; 3 productos del enlace no se pudieron agregar: ya no están en el menú o les falta elegir una opción.');
    expect(orderLinkMessage(0, 2)).toBe('Los productos del enlace no están en el menú de ahora o les falta elegir una opción. Mira el menú.');
  });
});
