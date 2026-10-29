import { Link } from 'react-router-dom';

/**
 * In place of «Volver a pedir el timbre» once a CFDI stayed «Timbrando» past the PAC's window (Fase 28K, ADR 0056): owners
 * and admins check in the SAT's portal whether it was stamped and say so in Reportes › CFDI emitidos; the rest of the team
 * is told who does.
 */
export default function StuckStampNote({ canReview }: { canReview: boolean }) {
  return (
    <p className="small text-warning-emphasis mb-0 mt-1">
      Se pidió hace más de 71 horas y el PAC ya no devuelve su timbre.{' '}
      {canReview ? (
        <>
          Revisa en el portal del SAT si se timbró y dilo en <Link to="/reports/issued-cfdis">CFDI emitidos</Link>.
        </>
      ) : (
        'El dueño o un administrador revisa en el portal del SAT si se timbró.'
      )}
    </p>
  );
}
