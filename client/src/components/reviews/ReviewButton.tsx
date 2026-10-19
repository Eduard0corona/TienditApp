interface ReviewButtonProps {
  businessName: string;
  /** The account already rated the business: the button opens its review. */
  reviewed: boolean;
  onClick: () => void;
}

/** «Calificar» on an order received or an appointment attended (Fase 24H), or «Tu reseña» once the business was rated. */
export default function ReviewButton({ businessName, reviewed, onClick }: ReviewButtonProps) {
  return (
    <button
      type="button"
      className="btn btn-sm btn-light"
      onClick={onClick}
      aria-label={reviewed ? `Tu reseña de ${businessName}` : `Calificar a ${businessName}`}
    >
      <i className={`${reviewed ? 'ri-star-fill' : 'ri-star-line'} me-1`} aria-hidden="true"></i>
      {reviewed ? 'Tu reseña' : 'Calificar'}
    </button>
  );
}
