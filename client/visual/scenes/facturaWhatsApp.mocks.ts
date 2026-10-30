import { asOption, type SceneOptions } from '../fixtures';
import { publicRoutes } from './publico-cuenta-marco.mocks';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// The page of a CFDI sent by WhatsApp (Fase 28M, ADR 0058): /factura#link, without an account. F-3 of Café La Esquina to
// Grupo Altavista, its link of a cancelled one, and any other link, unknown or expired.

/** The link of the WhatsApp message of F-3: 43 characters, as TienditApp makes them. */
export const CFDI_LINK = 'Wq3Lr8TzP1nKx5VbYc0Hd7Mf2Js9Ga4Ue6Io-_RtXp';
/** The link of a CFDI whose cancellation the business asked for. */
export const CANCELLED_CFDI_LINK = 'Bn7Yt2Kq9Lm4Xc1Vz6Hd3Pf8Js0Ga5Ue-_RwTiOpQs';

const F3 = {
  businessName: 'Café La Esquina',
  businessLogoUrl: '/visual-img/logo.svg',
  document: 0,
  number: 'F-3',
  uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF',
  amount: 1392,
  issuedOn: '2026-10-12',
  issuerName: 'CAFE LUNA',
  issuerRfc: 'CLU201005AB1',
  receiverName: 'GRUPO ALTAVISTA',
  receiverRfc: 'GAL1806043P5',
  stands: true,
  linkExpiresAt: '2027-01-10T19:25:00Z',
};

/** An error as the API answers it (ApiProblem). */
function problem(status: number, detail: string) {
  return { status, json: { status, title: 'Not Found', detail, code: 'not_found', error: detail, traceId: '00-visual-00' } };
}

const publicCfdiRoutes: ExtraRoutes = (data, helpers) => [
  ['POST', /^\/public\/cfdi$/, (_match, _query, body: { token?: string }) => {
    if (body?.token === CFDI_LINK) return helpers.ok(F3);
    if (body?.token === CANCELLED_CFDI_LINK) return helpers.ok({ ...F3, stands: false });
    return problem(404, 'No encontramos esa factura. El enlace pudo haber vencido: pídesela otra vez al negocio.');
  }],
  ...publicRoutes(data, helpers),
];

export const FACTURA_WHATSAPP_ROUTES = asOption(publicCfdiRoutes);
