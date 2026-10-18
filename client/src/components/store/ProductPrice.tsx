import { formatMoney } from '../../utils/money';
import { discountPercent } from '../../utils/storeMenu';

interface ProductPriceProps {
  price: number;
  compareAtPrice?: number | null;
  className?: string;
}

/** The price of a product of the online menu: with a compare-at price, the price before crossed out and how much less. */
export default function ProductPrice({ price, compareAtPrice, className = '' }: ProductPriceProps) {
  const discount = discountPercent(price, compareAtPrice);
  return (
    <p className={`pm-price-row ${className}`}>
      <span className="pm-price ta-money">{formatMoney(price)}</span>
      {compareAtPrice != null && (
        <s className="pm-compare ta-money"><span className="visually-hidden">antes </span>{formatMoney(compareAtPrice)}</s>
      )}
      {discount !== null && <span className="ta-chip ta-chip-new pm-discount">−{discount}%</span>}
    </p>
  );
}
