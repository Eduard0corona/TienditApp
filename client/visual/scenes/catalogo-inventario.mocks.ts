import { asOption, type DemoData } from '../fixtures';

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
