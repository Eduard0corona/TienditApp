import type { PublicMenuProduct } from '../../api/publicMenuApi';
import PhotoOrInitial from '../common/PhotoOrInitial';
import AddToOrderButton from './AddToOrderButton';
import ProductPrice from './ProductPrice';

interface MenuProductRowProps {
  product: PublicMenuProduct;
  inCart: number;
  /** Some product of the menu has a photo: the others show their initial, so the rows line up. */
  withPhotos: boolean;
  /** The menu takes orders now and the product is available now. */
  canAdd: boolean;
  onOpen: () => void;
  onAdd: () => void;
}

/**
 * A product of the online menu as the delivery apps show it (Fase 24E): its name, price and description on the left, its
 * photo on the right with the «+» on it. The name opens the product's sheet (the whole row does); the «+» adds it.
 */
export default function MenuProductRow({ product, inCart, withPhotos, canAdd, onOpen, onAdd }: MenuProductRowProps) {
  return (
    <article className={`pm-item ${product.availableNow ? '' : 'unavailable'}`}>
      <div className="pm-item-text">
        <h3 className="pm-item-name">
          <button type="button" className="pm-item-open" onClick={onOpen}>{product.name}</button>
        </h3>
        <ProductPrice price={product.price} compareAtPrice={product.compareAtPrice} />
        {product.description && <p className="pm-item-description">{product.description}</p>}
        {!product.availableNow && <p className="pm-item-note">No disponible ahora</p>}
      </div>
      {(withPhotos || canAdd) && (
        <div className={`pm-item-media ${withPhotos ? '' : 'no-photo'}`}>
          {withPhotos && <PhotoOrInitial src={product.imageUrl} name={product.name} className="pm-item-photo" />}
          {canAdd && <AddToOrderButton name={product.name} inCart={inCart} onClick={onAdd} />}
        </div>
      )}
    </article>
  );
}
