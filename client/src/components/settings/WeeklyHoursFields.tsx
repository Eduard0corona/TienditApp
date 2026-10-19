import type { DayOfWeek } from '../../types';
import {
  MAX_RANGES_PER_DAY,
  dayLabel,
  dayName,
  rangeHint,
  setDayOpen,
  type DayDraft,
  type TimeRange,
} from '../../utils/openingHours';
import { BUSINESS_HOURS_WORDS, type WeeklyHoursWords } from './weeklyHoursWords';
import './OpeningHoursCard.css';

interface WeeklyHoursFieldsProps {
  week: DayDraft[];
  onChange: (next: DayDraft[]) => void;
  /** Keeps the ids of two weeks on one page apart. */
  idPrefix: string;
  disabled?: boolean;
  words?: WeeklyHoursWords;
}

/**
 * The days of a week with up to two openings each, as the business's hours (OpeningHoursCard) and a person's own hours
 * (OnlineBookingStaffCard, Fase 24J) edit them. Only the draft: saving and checking it are the caller's.
 */
export default function WeeklyHoursFields({ week, onChange, idPrefix, disabled = false, words = BUSINESS_HOURS_WORDS }: WeeklyHoursFieldsProps) {
  const changeDay = (day: DayOfWeek, change: (ranges: TimeRange[]) => TimeRange[]) =>
    onChange(week.map((item) => (item.day === day ? { ...item, ranges: change(item.ranges) } : item)));
  const changeRange = (day: DayOfWeek, index: number, patch: Partial<TimeRange>) =>
    changeDay(day, (ranges) => ranges.map((range, position) => (position === index ? { ...range, ...patch } : range)));

  return (
    <fieldset disabled={disabled}>
      <legend className="visually-hidden">Días y horas</legend>
      <ul className="opening-hours-week list-unstyled mb-0">
        {week.map((item) => {
          const name = dayName(item.day);
          const switchId = `${idPrefix}-day-${item.day}`;
          return (
            <li key={item.day} className="opening-hours-day">
              <div className="form-check form-switch opening-hours-day-name">
                <input
                  id={switchId}
                  className="form-check-input"
                  type="checkbox"
                  role="switch"
                  checked={item.open}
                  onChange={(event) => onChange(setDayOpen(week, item.day, event.target.checked))}
                />
                <label className="form-check-label" htmlFor={switchId}>{dayLabel(item.day)}</label>
              </div>
              {item.open ? (
                <div className="opening-hours-ranges">
                  {item.ranges.map((range, index) => {
                    const which = index === 0 ? '' : ', segundo horario';
                    const hint = rangeHint(range);
                    return (
                      <div key={index} className="opening-hours-range">
                        <input
                          type="time"
                          className="form-control form-control-sm"
                          aria-label={`${words.opens} el ${name}${which}`}
                          value={range.opens}
                          onChange={(event) => changeRange(item.day, index, { opens: event.target.value })}
                        />
                        <span className="text-muted small" aria-hidden="true">a</span>
                        <input
                          type="time"
                          className="form-control form-control-sm"
                          aria-label={`${words.closes} el ${name}${which}`}
                          value={range.closes}
                          onChange={(event) => changeRange(item.day, index, { closes: event.target.value })}
                        />
                        {index > 0 && (
                          <button
                            type="button"
                            className="btn btn-sm btn-light"
                            aria-label={`Quitar el segundo horario del ${name}`}
                            title="Quitar este horario"
                            onClick={() => changeDay(item.day, (ranges) => ranges.filter((_, position) => position !== index))}
                          >
                            <i className="ri-close-line" aria-hidden="true"></i>
                          </button>
                        )}
                        {hint && <span className="small text-muted opening-hours-hint">{hint}</span>}
                        {index === item.ranges.length - 1 && item.ranges.length < MAX_RANGES_PER_DAY && (
                          <button
                            type="button"
                            className="btn btn-link btn-sm opening-hours-add"
                            aria-label={`Agregar horario al ${name}`}
                            onClick={() => changeDay(item.day, (ranges) => [...ranges, { opens: '', closes: '' }])}
                          >
                            <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar horario
                          </button>
                        )}
                      </div>
                    );
                  })}
                </div>
              ) : (
                <span className="small text-muted opening-hours-closed">{words.closed}</span>
              )}
            </li>
          );
        })}
      </ul>
    </fieldset>
  );
}
