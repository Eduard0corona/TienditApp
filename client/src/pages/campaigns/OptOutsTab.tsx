import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { messagingApi } from '../../api/messagingApi';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { OptOutSource, type MessagingOptOutDto, type PaginatedList } from '../../types';
import { formatDateTime } from '../../utils/dates';

const PAGE_SIZE = 20;
const SEARCH_DELAY_MS = 250;

/** Customers who asked not to receive campaigns or reminders (BAJA by WhatsApp, or recorded here). */
export default function OptOutsTab() {
  const toast = useToast();
  const { confirm } = useConfirm();
  const addFormId = useId();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  /** The last list read, with the search it was read for. */
  const [shown, setShown] = useState<{ list: PaginatedList<MessagingOptOutDto>; search: string } | null>(null);
  const [phone, setPhone] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [formError, setFormError] = useState('');
  const latestRead = useRef(0);
  const data = shown?.list ?? null;

  const load = useCallback(async () => {
    const read = ++latestRead.current;
    const term = search.trim();
    try {
      const { data: list } = await messagingApi.getOptOuts({ search: term || undefined, pageNumber: page, pageSize: PAGE_SIZE });
      // Only the last reading is shown: an answer to what was typed before arrives late.
      if (read !== latestRead.current) return;
      // The last one of the last page was reactivated: that page no longer exists, so the one before is read.
      if (list.items.length === 0 && page > 1 && list.totalPages < page) {
        setPage(Math.max(1, list.totalPages));
        return;
      }
      setShown({ list, search: term });
      setError('');
    } catch (err: unknown) {
      if (read !== latestRead.current) return;
      // What is left would be another page or another search: it says it could not be read instead.
      setShown(null);
      setError(getApiErrorMessage(err, 'No se pudieron cargar las bajas.'));
    }
  }, [page, search]);

  useEffect(() => {
    const id = window.setTimeout(() => void load(), SEARCH_DELAY_MS);
    return () => window.clearTimeout(id);
  }, [load]);

  const add = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!phone.trim() || busy) return;
    setBusy(true);
    setFormError('');
    try {
      await messagingApi.setOptOut(phone.trim(), true);
      toast.success('Baja registrada: ya no recibirá campañas ni recordatorios.');
      setPhone('');
      await load();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, 'No se pudo registrar la baja.'));
    } finally {
      setBusy(false);
    }
  };

  const resubscribe = async (optOut: MessagingOptOutDto) => {
    const accepted = await confirm({
      title: 'Volver a enviar mensajes',
      message: `Solo hazlo si el cliente ${optOut.phone} te lo pidió. Volverá a recibir campañas y recordatorios.`,
      confirmLabel: 'Reactivar',
    });
    if (!accepted) return;
    try {
      await messagingApi.setOptOut(optOut.phone, false);
      toast.success('El cliente volverá a recibir mensajes.');
      await load();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo reactivar.'));
    }
  };

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mb-1">Bajas</h4>
        <p className="text-muted small mb-3">
          Quien escribe BAJA deja de recibir campañas y recordatorios; con ALTA vuelve. Los avisos de sus pedidos siguen llegando.
        </p>
        {/* Only the phone and its button belong to the form: Enter in the search does not register a baja. */}
        <form id={addFormId} onSubmit={(event) => void add(event)} />
        <div className="row g-2 mb-3">
          <div className="col-sm-5">
            <label className="visually-hidden" htmlFor="optout-phone">Teléfono</label>
            <input id="optout-phone" form={addFormId} className="form-control" inputMode="tel" maxLength={30}
              placeholder="Teléfono del cliente" value={phone} onChange={(event) => setPhone(event.target.value)} />
          </div>
          <div className="col-sm-3">
            <button type="submit" form={addFormId} className="btn btn-outline-danger w-100" disabled={busy || !phone.trim()}>
              Registrar baja
            </button>
          </div>
          <div className="col-sm-4">
            <label className="visually-hidden" htmlFor="optout-search">Buscar</label>
            <input id="optout-search" className="form-control" inputMode="tel" placeholder="Buscar por teléfono" value={search}
              onChange={(event) => { setSearch(event.target.value); setPage(1); }} />
          </div>
        </div>
        {formError && <div className="alert alert-danger" role="alert">{formError}</div>}
        {error && (
          <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
            <span className="me-auto">{error}</span>
            {/* Reading again takes the notice away while it loads. */}
            <button type="button" className="btn btn-sm btn-light" onClick={() => { setError(''); void load(); }}>
              Reintentar
            </button>
          </div>
        )}
        {!data ? (
          // A list that could not be read says so above, instead of loading forever.
          error ? null : <p className="text-muted mb-0" role="status">Cargando…</p>
        ) : data.items.length === 0 ? (
          <p className="text-muted mb-0">
            {shown?.search ? 'Ningún teléfono coincide con la búsqueda.' : 'Nadie ha pedido dejar de recibir mensajes.'}
          </p>
        ) : (
          <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead>
                <tr>
                  <th>Teléfono</th>
                  <th>Desde</th>
                  <th>Cómo</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((optOut) => (
                  <tr key={optOut.id}>
                    <td className="fw-semibold">{optOut.phone}</td>
                    <td className="small">{formatDateTime(optOut.optedOutAt)}</td>
                    <td className="small">{optOut.source === OptOutSource.CustomerKeyword ? 'Escribió BAJA' : 'Registrada por el negocio'}</td>
                    <td className="text-end">
                      <button type="button" className="btn btn-sm btn-light" onClick={() => void resubscribe(optOut)}>Reactivar</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {data && data.totalPages > 1 && (
          <div className="d-flex justify-content-end gap-2 mt-2">
            <button type="button" className="btn btn-sm btn-light" disabled={!data.hasPreviousPage} onClick={() => setPage(page - 1)}>Anterior</button>
            <span className="small align-self-center">{data.pageNumber} / {data.totalPages}</span>
            <button type="button" className="btn btn-sm btn-light" disabled={!data.hasNextPage} onClick={() => setPage(page + 1)}>Siguiente</button>
          </div>
        )}
      </div>
    </div>
  );
}
