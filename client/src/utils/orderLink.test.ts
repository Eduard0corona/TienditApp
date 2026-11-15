import { describe, expect, it } from 'vitest';
import { assistantOrderMessage, assistantOrderTokenOf, orderLinkMessage, orderLinkOf } from './orderLink';

const LATTE = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
const GRANDE = '9b2d7f1e-1c3a-4f5b-8d6e-7a8b9c0d1e2f';
const AVENA = '0c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f';
const CONCHA = '7d8e9f0a-1b2c-4d3e-9f4a-5b6c7d8e9f0a';

const outcome = (changes: Partial<Parameters<typeof orderLinkMessage>[0]> = {}) =>
  ({ kept: 2, notOnMenu: 0, invalid: 0, replaced: false, paused: false, ...changes });

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

  it('takes an option once, as the API does, and ids in capitals as the menu writes them', () => {
    expect(orderLinkOf([`${LATTE.toUpperCase()}:1:${GRANDE}:${GRANDE.toUpperCase()}`])?.lines).toEqual([
      { productId: LATTE, optionIds: [GRANDE], quantity: 1 },
    ]);
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

describe('orderLinkMessage (Fase 32A, 32C)', () => {
  const send = 'Revísalo, escribe tus datos y envíalo: todavía no le llega al negocio.';

  it('says whose order it is and that it has not reached the business, when everything went in an empty cart', () => {
    expect(orderLinkMessage(outcome())).toBe(`Este es el pedido del enlace. ${send}`);
    expect(orderLinkMessage(outcome({ fromAssistant: true }))).toBe(`Tu asistente preparó este pedido. ${send}`);
  });

  it('says what the menu could not take, apart from what the link did not say right, in singular and plural', () => {
    expect(orderLinkMessage(outcome({ notOnMenu: 1 }))).toBe(
      `Este es el pedido del enlace. Agregamos lo que sigue disponible; 1 producto ya no está en el menú o le falta elegir una opción. ${send}`,
    );
    expect(orderLinkMessage(outcome({ notOnMenu: 2, invalid: 1 }))).toBe(
      'Este es el pedido del enlace. Agregamos lo que sigue disponible; 2 productos ya no están en el menú o les falta elegir una opción; 1 parte del enlace no se entiende (un id o una cantidad que no es válida). ' + send,
    );
    expect(orderLinkMessage(outcome({ invalid: 3 }))).toBe(
      `Este es el pedido del enlace. Agregamos lo que sigue disponible; 3 partes del enlace no se entienden (un id o una cantidad que no es válida). ${send}`,
    );
  });

  it('says when nothing went in, and whose order it was', () => {
    expect(orderLinkMessage(outcome({ kept: 0, notOnMenu: 2 }))).toBe(
      'No pudimos agregar los productos del enlace: 2 productos ya no están en el menú o les falta elegir una opción. Mira el menú.',
    );
    expect(orderLinkMessage(outcome({ kept: 0, notOnMenu: 1, fromAssistant: true }))).toBe(
      'No pudimos agregar los productos que preparó tu asistente: 1 producto ya no está en el menú o le falta elegir una opción. Mira el menú.',
    );
  });

  it('says the cart that was there gave way to the link, and that the order waits while the store takes none', () => {
    expect(orderLinkMessage(outcome({ replaced: true }))).toBe(`Cambiamos el pedido que tenías por el del enlace. ${send}`);
    expect(orderLinkMessage(outcome({ replaced: true, fromAssistant: true }))).toBe(
      `Tu asistente preparó este pedido. Cambiamos el pedido que tenías por este. ${send}`,
    );
    // Waiting, it is not to send now.
    expect(orderLinkMessage(outcome({ paused: true, notOnMenu: 1 }))).toBe(
      'Guardamos el pedido del enlace: envíalo cuando este negocio vuelva a recibir pedidos. No agregamos todo; 1 producto ya no está en el menú o le falta elegir una opción.',
    );
    expect(orderLinkMessage(outcome({ paused: true, fromAssistant: true }))).toBe(
      'Tu asistente preparó este pedido. Guardamos el pedido: envíalo cuando este negocio vuelva a recibir pedidos.',
    );
  });
});

describe('assistantOrderTokenOf (Fase 32B)', () => {
  const TOKEN = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';

  it('reads the token of the fragment of an assistant\'s link, with or without its #', () => {
    expect(assistantOrderTokenOf(`#pedido=${TOKEN}`)).toBe(TOKEN);
    expect(assistantOrderTokenOf(`pedido=${TOKEN}`)).toBe(TOKEN);
    expect(assistantOrderTokenOf(`#otro=1&pedido=${TOKEN}`)).toBe(TOKEN);
  });

  it('takes nothing that is not one of its tokens', () => {
    expect(assistantOrderTokenOf('')).toBeNull();
    expect(assistantOrderTokenOf('#pedido=')).toBeNull();
    expect(assistantOrderTokenOf(`#pedido=${TOKEN.slice(1)}`)).toBeNull();
    expect(assistantOrderTokenOf(`#pedido=${TOKEN}A`)).toBeNull();
    expect(assistantOrderTokenOf(`#pedido=${TOKEN.slice(1)}+`)).toBeNull();
    expect(assistantOrderTokenOf(`#seguimiento=${TOKEN}`)).toBeNull();
  });

  it('says an order sent, expired or not found', () => {
    expect(assistantOrderMessage('sent')).toBe('Este pedido ya se envió. Síguelo con el enlace que te dio la página al enviarlo.');
    expect(assistantOrderMessage('expired')).toBe('Este pedido venció. Pídele al asistente que lo prepare otra vez, o arma tu pedido aquí.');
    expect(assistantOrderMessage('missing')).toBe('No encontramos ese pedido. Revisa el enlace, o arma tu pedido aquí.');
  });
});
