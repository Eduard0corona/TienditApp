interface AddToOrderButtonProps {
  name: string;
  /** How many of the product the order has, in any of its lines: the button shows it once there is one. */
  inCart: number;
  onClick: () => void;
}

/** The round «+» on the photo of a product of the online menu, as in the delivery apps: it says how many you have. */
export default function AddToOrderButton({ name, inCart, onClick }: AddToOrderButtonProps) {
  return (
    <button type="button" className={`pm-add ${inCart > 0 ? 'in-cart' : ''}`} onClick={onClick} aria-label={`Agregar ${name}`}>
      {inCart > 0 ? <span className="ta-money">{inCart}</span> : <i className="ri-add-line" aria-hidden="true"></i>}
    </button>
  );
}
