import { useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { useHomeVisitSettings, useSaveHomeVisitSettings } from '../../api/homeVisits';
import { useToast } from '../../hooks/useToast';
import { MAX_TRAVEL_MINUTES, TRAVEL_MINUTES_MESSAGE } from '../../utils/homeVisits';

/**
 * Visitas a domicilio (Fase 22E), for a business with the agenda: its delivery zones are where it goes to its customers,
 * and the fee of a zone is the charge for the visit. Between a visit and another appointment of the same person the agenda
 * keeps the travel time set here (0 for none); appointments already booked stay as they are.
 */
export default function HomeVisitTravelCard() {
  const toast = useToast();
  const settings = useHomeVisitSettings();
  const save = useSaveHomeVisitSettings();
  // What the person typed; null while it is the value saved.
  const [typed, setTyped] = useState<string | null>(null);
  const saved = settings.data?.travelMinutes;
  const value = typed ?? (saved != null ? String(saved) : '');
  const minutes = Number(value);
  const valid = value.trim() !== '' && Number.isInteger(minutes) && minutes >= 0 && minutes <= MAX_TRAVEL_MINUTES;
  const changed = valid && minutes !== saved;

  const submit = async () => {
    if (!valid) return;
    try {
      await save.mutateAsync(minutes);
      setTyped(null);
      toast.success('Tiempo de traslado guardado.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo guardar el tiempo de traslado.'));
    }
  };

  return (
    <div className="card">
      <div className="card-body">
        <h2 className="h5">Visitas a domicilio</h2>
        <p className="text-muted mb-3">
          Tus citas a domicilio usan estas zonas: llegas a sus códigos postales y su costo de envío es el cargo por visita. Entre
          una visita y otra cita de la misma persona, la agenda deja el tiempo de traslado que indiques aquí.
        </p>
        {settings.isError && (
          <div className="alert alert-danger">{getApiErrorMessage(settings.error, 'No se pudo cargar el tiempo de traslado.')}</div>
        )}
        <form
          className="d-flex flex-wrap align-items-end gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          <div>
            <label className="form-label" htmlFor="home-visit-travel">Traslado entre citas (minutos)</label>
            <input
              id="home-visit-travel"
              type="number"
              className={`form-control${value !== '' && !valid ? ' is-invalid' : ''}`}
              style={{ maxWidth: '10rem' }}
              min={0}
              max={MAX_TRAVEL_MINUTES}
              step={5}
              inputMode="numeric"
              value={value}
              disabled={settings.isPending || save.isPending}
              onChange={(event) => setTyped(event.target.value)}
              aria-describedby="home-visit-travel-help"
            />
          </div>
          <button type="submit" className="btn btn-light" disabled={!changed || save.isPending}>
            {save.isPending ? 'Guardando…' : 'Guardar'}
          </button>
          <div id="home-visit-travel-help" className="form-text w-100 mt-0">
            {value !== '' && !valid ? TRAVEL_MINUTES_MESSAGE : '0 si tu equipo no necesita tiempo entre citas.'}
          </div>
        </form>
      </div>
    </div>
  );
}
