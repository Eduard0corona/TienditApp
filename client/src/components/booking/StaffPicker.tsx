import type { Ref } from 'react';
import type { PublicBookingStaff } from '../../api/publicMenuApi';
import { useScrollFade } from '../../hooks/useScrollFade';
import PhotoOrInitial from '../common/PhotoOrInitial';

interface StaffPickerProps {
  /** Who the customer chooses among, by the name the business gave them for customers. */
  people: PublicBookingStaff[];
  /** The person chosen, or null for whoever is free. */
  personId: string | null;
  onChoose: (id: string | null) => void;
  /** «Sin preferencia», which keeps the focus when the person chosen goes away. */
  anyoneRef?: Ref<HTMLButtonElement>;
  /** What the group is for a screen reader: «Quién te atiende», «Quién va». */
  label: string;
}

/**
 * «¿Con quién?» (Fase 24J, ADR 0032): the people a business books online, with the photo it chose for them (Fase 31I)
 * or their initial, or no preference. The booking page and the request page of a visit (Fase 29B) choose the same way.
 */
export default function StaffPicker({ people, personId, onChoose, anyoneRef, label }: StaffPickerProps) {
  const { ref, fadeClass } = useScrollFade<HTMLDivElement>(people.length);

  return (
    <>
      <h2 className="pm-section-title pb-step">¿Con quién?</h2>
      <div ref={ref} className={`pb-people ta-scroll-fade ${fadeClass}`} role="group" aria-label={label}>
        <button
          ref={anyoneRef}
          type="button"
          className={`btn pb-person ${personId === null ? 'btn-primary' : 'btn-light'}`}
          aria-pressed={personId === null}
          onClick={() => onChoose(null)}
        >
          <span className="d-block fw-semibold">Sin preferencia</span>
          <span className="d-block small">Quien esté libre</span>
        </button>
        {people.map((item) => {
          const active = personId === item.id;
          return (
            <button
              key={item.id}
              type="button"
              className={`btn pb-person ${active ? 'btn-primary' : 'btn-light'}`}
              aria-pressed={active}
              onClick={() => onChoose(item.id)}
            >
              <PhotoOrInitial src={item.photoUrl} name={item.name} className="pb-person-initial" />
              <span className="d-block fw-semibold text-truncate pb-person-name">{item.name}</span>
            </button>
          );
        })}
      </div>
    </>
  );
}
