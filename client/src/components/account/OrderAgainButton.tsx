interface OrderAgainButtonProps {
  order: { id: string; orderNumber: string; businessName: string };
  /** The order whose store is opening (`useOrderAgain`), if any: this button says so, and the others wait. */
  opening: string | null;
  onClick: () => void;
  className: string;
}

/** «Pedir otra vez» (Fase 24B); while the account keeps the cart (Fase 31P), «Abriendo la tienda…» and nothing to press. */
export function OrderAgainButton({ order, opening, onClick, className }: OrderAgainButtonProps) {
  const openingThis = opening === order.id;
  return (
    <button
      type="button"
      className={className}
      onClick={onClick}
      disabled={opening !== null}
      aria-busy={openingThis || undefined}
      aria-label={openingThis ? `Abriendo la tienda de ${order.businessName}…` : `Pedir otra vez ${order.orderNumber} de ${order.businessName}`}
    >
      {openingThis ? (
        <>
          <span className="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>Abriendo la tienda…
        </>
      ) : (
        <>
          <i className="ri-restart-line me-1" aria-hidden="true"></i>Pedir otra vez
        </>
      )}
    </button>
  );
}
