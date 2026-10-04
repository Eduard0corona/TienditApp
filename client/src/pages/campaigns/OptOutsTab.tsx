import { useCallback, useEffect, useState } from 'react';
import { messagingApi } from '../../api/messagingApi';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { OptOutSource, type MessagingOptOutDto, type PaginatedList } from '../../types';
import { formatDateTime } from '../../utils/dates';

/** Customers who asked not to receive campaigns or reminders (BAJA by WhatsApp, or recorded here). */
export default function OptOutsTab() {
  const toast = useToast();
  const { confirm } = useConfirm();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [data, setData] = useState<PaginatedList<MessagingOptOutDto> | null>(null);
  const [phone, setPhone] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    try {
      const { data: list } = await messagingApi.getOptOuts({ search: search.trim() || undefined, pageNumber: page, pageSize: 20 });
      setData(list);
      setError('');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar las bajas.'));
    }
  }, [page, search]);

  useEffect(() => {
    const id = window.setTimeout(() => void load(), 250);
    return () => window.clearTimeout(id);
  }, [load]);

  const add = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!phone.trim() || busy) return;
    setBusy(true);
    try {
      await messagingApi.setOptOut(phone.trim(), true);
      toast.success('Baja registrada: ya no recibirá campañas ni recordatorios.');
      setPhone('');
      await load();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo registrar la baja.'));
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
        <form className="row g-2 mb-3" onSubmit={(event) => void add(event)}>
          <div className="col-sm-5">
            <label className="visually-hidden" htmlFor="optout-phone">Teléfono</label>
            <input id="optout-phone" className="form-control" inputMode="tel" maxLength={30} placeholder="Teléfono del cliente"
              value={phone} onChange={(event) => setPhone(event.target.value)} />
          </div>
          <div className="col-sm-3">
            <button type="submit" className="btn btn-outline-danger w-100" disabled={busy || !phone.trim()}>Registrar baja</button>
          </div>
          <div className="col-sm-4">
            <label className="visually-hidden" htmlFor="optout-search">Buscar</label>
            <input id="optout-search" className="form-control" inputMode="tel" placeholder="Buscar por teléfono" value={search}
              onChange={(event) => { setSearch(event.target.value); setPage(1); }} />
          </div>
        </form>
        {error && <div className="alert alert-danger">{error}</div>}
        {!data ? (
          <p className="text-muted mb-0">Cargando…</p>
        ) : data.items.length === 0 ? (
          <p className="text-muted mb-0">Nadie ha pedido dejar de recibir mensajes.</p>
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
