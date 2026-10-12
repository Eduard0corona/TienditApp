import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { withRappiPrices } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// The demo photos in mocks/img, by product name: the lists then show photos and the placeholder side by side.
const PHOTOS: Record<string, string> = {
  Capuchino: 'capuchino',
  Latte: 'latte',
  'Frappé moka': 'frappe',
  'Chilaquiles verdes': 'chilaquiles',
  'Croissant de mantequilla': 'croissant',
  'Concha de vainilla': 'concha',
};

/** Gives the products that have a demo photo their image URLs (ProductImageUrlsDto), all sizes the same picture. */
export function withProductPhotos(data: DemoData) {
  for (const product of data.products) {
    const photo = PHOTOS[product.name];
    if (!photo) continue;
    const url = `/visual-img/${photo}.svg`;
    product.images = { original: url, large: url, medium: url, small: url, thumbnail: url };
  }
}

/**
 * `withProductPhotos` as the value of `test.use({ prepareData })`. Playwright takes any function given to test.use as
 * the fixture that builds the option, so the function the option holds goes through one.
 */
export const productPhotos = asOption(withProductPhotos);

// OrderChannel as the contract numbers the delivery platforms.
const RAPPI = 3;
const UBER_EATS = 4;
const DIDI_FOOD = 5;

/** The business set up the three delivery platforms and takes orders of Rappi and Uber Eats; DiDi Food is off. */
const platformRoutes: ExtraRoutes = (_data, { ok }) => [
  ['GET', /^\/delivery-platforms\/channels$/, () => ok([
    { channel: RAPPI, isActive: true },
    { channel: UBER_EATS, isActive: true },
    { channel: DIDI_FOOD, isActive: false },
  ])],
];

export const PLATFORM_ROUTES = asOption(platformRoutes);

/** Its prices in the Rappi app (see withRappiPrices): the rest of the products charge the menu's there. */
export const RAPPI_PRICES = asOption(withRappiPrices);
