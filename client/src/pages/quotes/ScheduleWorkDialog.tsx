import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { employeesApi } from '../../api/employeesApi';
import { getApiErrorMessage } from '../../api/errors';
import { quotesApi, type Quote } from '../../api/quotesApi';
import { businessDateTimeToIso, toBusinessDateTimeInput } from '../../utils/dates';

interface ScheduleWorkDialogProps {
  /** A converted quote: its order is where the job is charged. */
  quote: Quote;
  onClose: () => void;
  /** The job was agendado: the quote as the server has it now, with the appointments of its job. */
  onScheduled: (scheduled: Quote) => void;
}

/** How long the job lasts unless the person says otherwise: two hours. */
const DEFAULT_LENGTH_MS = 2 * 60 * 60 * 1000;

/**
 * Agendar the job of the order the quote became (Fase 29F, ADR 0053 point 17): when it starts and ends, read in the business's
 * time zone, who does it and what the team should know. It happens at the place of the visit the quote came from and is
 * charged in its order, never on its own. A job that takes more days is agendado again, one appointment per day.
 */
export default function ScheduleWorkDialog({ quote, onClose, onScheduled }: ScheduleWorkDialogProps) {
  const titleId = useId();
  const queryClient = useQueryClient();
  const [startAt, setStartAt] = useState('');
  const [endAt, setEndAt] = useState('');
  const [staffUserId, setStaffUserId] = useState('');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState('');
  const staff = useQuery({
    queryKey: ['employees', 'active'],
    queryFn: async () => (await employeesApi.getAll({ status: 'Active', pageSize: 200 })).data.items.filter((member) => member.isActive),
  });
  const postalCode = quote.appointment?.postalCode;

  const schedule = useMutation({
    mutationFn: () => quotesApi.scheduleWork(quote.id, {
      startAt: businessDateTimeToIso(startAt),
      endAt: businessDateTimeToIso(endAt),
      staffUserId: staffUserId || null,
      notes: notes.trim() || null,
    }),
    onSuccess: onScheduled,
    onError: (err) => {
      setError(getApiErrorMessage(err, 'No se pudo agendar el trabajo.'));
      // Its order may have been cancelled meanwhile: the page shows the quote as it is now.
      void queryClient.invalidateQueries({ queryKey: ['quotes', 'detail', quote.id] });
    },
  });

  // A start without an end, or an end before it, takes the usual length.
  const changeStart = (value: string) => {
    setStartAt(value);
    if (!value) return;
    const start = Date.parse(businessDateTimeToIso(value));
    if (!endAt || Date.parse(businessDateTimeToIso(endAt)) <= start) setEndAt(toBusinessDateTimeInput(start + DEFAULT_LENGTH_MS));
  };

  return (
    <Modal show onHide={() => !schedule.isPending && onClose()} centered aria-labelledby={titleId}>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          setError('');
          schedule.mutate();
        }}
      >
        <Modal.Header closeButton={!schedule.isPending}>
          <Modal.Title id={titleId} as="h2" className="h5">Agendar el trabajo</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="mb-2">
            {postalCode
              ? `En la dirección de la visita (CP ${postalCode}), sin cargo por ir.`
              : 'La cotización no vino de una visita: si el trabajo es a domicilio, agrega la dirección después en la Agenda.'}{' '}
            Se cobra en la orden {quote.order?.orderNumber}, no aparte.
          </p>
          <fieldset disabled={schedule.isPending}>
            <div className="row g-2">
              <div className="col-sm-6">
                <label className="form-label" htmlFor="work-start">Empieza</label>
                <input
                  id="work-start"
                  className="form-control"
                  type="datetime-local"
                  required
                  value={startAt}
                  onChange={(event) => changeStart(event.target.value)}
                />
              </div>
              <div className="col-sm-6">
                <label className="form-label" htmlFor="work-end">Termina</label>
                <input
                  id="work-end"
                  className="form-control"
                  type="datetime-local"
                  required
                  value={endAt}
                  onChange={(event) => setEndAt(event.target.value)}
                />
              </div>
            </div>
            <label className="form-label mt-2" htmlFor="work-staff">Quién lo hace</label>
            <select id="work-staff" className="form-select" value={staffUserId} onChange={(event) => setStaffUserId(event.target.value)}>
              <option value="">Sin asignar</option>
              {(staff.data ?? []).map((member) => (
                <option key={member.userId} value={member.userId}>{member.firstName} {member.lastName}</option>
              ))}
            </select>
            <label className="form-label mt-2" htmlFor="work-notes">Notas para el equipo</label>
            <textarea
              id="work-notes"
              className="form-control"
              rows={2}
              maxLength={2000}
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
            />
          </fieldset>
          {error && <div className="alert alert-danger mt-3 mb-0" role="alert">{error}</div>}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose} disabled={schedule.isPending}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={schedule.isPending}>
            {schedule.isPending ? 'Agendando…' : 'Agendar'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
