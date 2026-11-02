import PrivatePhoto from '../common/PrivatePhoto';
import { toPrivateFilePath } from '../../utils/privateFiles';

interface VisitProblemProps {
  /** What happens, in the customer's words; null when they did not request the visit online. */
  problem?: string | null;
  /** The private paths of the photos they sent with it (Fase 29H), in the order sent. */
  photos: string[];
  /** The size of each thumbnail. */
  size?: number;
}

/**
 * What the customer said happens and the photos they sent with the visit they requested online (Fase 29H, ADR 0062): for
 * the business only, each photo fetched with the session and opened full in a new tab.
 */
export default function VisitProblem({ problem, photos, size = 96 }: VisitProblemProps) {
  // Only a path the app creates opens: never an address a record could carry.
  const paths = photos.map((photo) => toPrivateFilePath(photo)).filter((path): path is string => path !== null);
  return (
    <>
      {problem && <p className="mb-2" style={{ whiteSpace: 'pre-line' }}>{problem}</p>}
      {paths.length > 0 && (
        <ul className="list-unstyled d-flex flex-wrap gap-2 mb-0">
          {paths.map((path, index) => (
            <li key={path}>
              <PrivatePhoto path={path} alt={`Foto ${index + 1} de lo que pasa`} linked className="rounded border" style={{ width: size, height: size }} />
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
