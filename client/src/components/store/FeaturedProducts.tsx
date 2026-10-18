import { useId } from 'react';
import type { PublicMenuProduct } from '../../api/publicMenuApi';
import { useScrollFade } from '../../hooks/useScrollFade';
import PhotoOrInitial from '../common/PhotoOrInitial';
import AddToOrderButton from './AddToOrderButton';
import ProductPrice from './ProductPrice';

interface FeaturedProductsProps {
  products: PublicMenuProduct[];
  inCart: (product: PublicMenuProduct) => number;
  canAdd: (product: PublicMenuProduct) => boolean;
  onOpen: (product: PublicMenuProduct) => void;
  onAdd: (product: PublicMenuProduct) => void;
}

/**
 * «Destacados» at the top of the online menu (Fase 24E): the products the business marked as featured, in a row of
 * large photos that slides sideways, as the delivery apps open a store. They are in their categories too.
 */
export default function FeaturedProducts({ products, inCart, canAdd, onOpen, onAdd }: FeaturedProductsProps) {
  const titleId = useId();
  const { ref, fadeClass } = useScrollFade<HTMLDivElement>(products.length);
  return (
    <section className="pm-featured" aria-labelledby={titleId}>
      <h2 id={titleId} className="pm-section-title">Destacados</h2>
      <div ref={ref} className={`pm-featured-row ta-scroll-fade ${fadeClass}`}>
        {products.map((product) => (
          <article key={product.id} className={`pm-feature ${product.availableNow ? '' : 'unavailable'}`}>
            <div className="pm-feature-media">
              <PhotoOrInitial src={product.imageUrl} name={product.name} className="pm-feature-photo" />
              {canAdd(product) && <AddToOrderButton name={product.name} inCart={inCart(product)} onClick={() => onAdd(product)} />}
            </div>
            <h3 className="pm-feature-name">
              <button type="button" className="pm-item-open" onClick={() => onOpen(product)}>{product.name}</button>
            </h3>
            <ProductPrice price={product.price} compareAtPrice={product.compareAtPrice} />
          </article>
        ))}
      </div>
    </section>
  );
}
