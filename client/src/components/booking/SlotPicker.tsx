import type { PublicBookingSlot, PublicBookingSlots } from '../../api/publicMenuApi';
import { useScrollFade } from '../../hooks/useScrollFade';
import { shortDayLabel } from '../../utils/onlineBooking';

const horarios = (count: number) => (count === 1 ? '1 horario' : `${count} horarios`);

interface SlotPickerProps {
  /** The week the server answered: its days, each with its free times, and where the next one starts. */
  week: PublicBookingSlots;
  /** A week is being read: its buttons wait. */
  fetching: boolean;
  /** There is a week before this one to go back to. */
  canGoBack: boolean;
  /** Opens the week that starts on `next`, or the one before with null. */
  onWeekChange: (next: string | null) => void;
  /** The day picked; the first one with free times when it has none. */
  day: string | null;
  onDayChange: (date: string) => void;
  /** The start of the time picked, or null. */
  chosen: string | null;
  onChoose: (slot: PublicBookingSlot) => void;
  /** What the page says once there are no more days to book online. */
  noMoreDays: string;
}

/**
 * The days of a week and the free times of the day picked, as the server offers them (Fase 24D): the booking page and
 * the request page of a visit (Fase 29B) pick the same way. A time after midnight, in the opening of the day before,
 * says its own day.
 */
export default function SlotPicker({ week, fetching, canGoBack, onWeekChange, day, onDayChange, chosen, onChoose, noMoreDays }: SlotPickerProps) {
  const days = week.days;
  // The day picked while it has free times; otherwise the first of the week that has.
  const shownDay = days.find((item) => item.date === day && item.slots.length > 0) ?? days.find((item) => item.slots.length > 0) ?? null;
  const { ref: daysRef, fadeClass: daysFade } = useScrollFade<HTMLDivElement>(days.length);

  return (
    <>
      <div ref={daysRef} className={`pb-days ta-scroll-fade ${daysFade}`} role="group" aria-label="Días de la semana">
        {days.map((item) => {
          const active = shownDay?.date === item.date;
          return (
            <button
              key={item.date}
              type="button"
              className={`btn pb-day ${active ? 'btn-primary' : 'btn-light'}`}
              aria-pressed={active}
              disabled={item.slots.length === 0}
              onClick={() => onDayChange(item.date)}
            >
              {/* Its name says what shows, and the whole date for a screen reader. */}
              <span className="d-block fw-semibold">{shortDayLabel(item.date)}</span>
              <span className="d-block small">{item.slots.length > 0 ? horarios(item.slots.length) : 'Sin horarios'}</span>
              <span className="visually-hidden">, {item.label}</span>
            </button>
          );
        })}
      </div>
      <div className="pb-weeks">
        <button type="button" className="btn btn-sm btn-light" disabled={!canGoBack || fetching} onClick={() => onWeekChange(null)}>
          <i className="ri-arrow-left-s-line" aria-hidden="true"></i>Semana anterior
        </button>
        <button
          type="button"
          className="btn btn-sm btn-light"
          disabled={!week.nextFrom || fetching}
          onClick={() => onWeekChange(week.nextFrom ?? null)}
        >
          Semana siguiente<i className="ri-arrow-right-s-line" aria-hidden="true"></i>
        </button>
      </div>

      {shownDay ? (
        <>
          <h2 className="pm-section-title pb-step">Elige la hora · {shownDay.label}</h2>
          <div className="pb-slots" role="group" aria-label={`Horarios libres del ${shownDay.label}`}>
            {shownDay.slots.map((slot) => {
              const active = chosen === slot.startAt;
              return (
                <button
                  key={slot.startAt}
                  type="button"
                  className={`btn ${active ? 'btn-primary' : 'btn-light'}`}
                  aria-pressed={active}
                  onClick={() => onChoose(slot)}
                >
                  {slot.label}
                  {/* After midnight, in the opening of the day before: it says its own day. */}
                  {slot.date !== shownDay.date && ` · ${shortDayLabel(slot.date)}`}
                </button>
              );
            })}
          </div>
        </>
      ) : (
        <p className="text-muted text-center py-4 mb-0">
          {days.length === 0 ? noMoreDays : 'No hay horarios libres esta semana.'}
          {week.nextFrom && ' Mira la semana siguiente.'}
        </p>
      )}
    </>
  );
}
