import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import type { HomeVisitRequestsSettings } from '../../api/appointmentsApi';
import { getApiErrorMessage } from '../../api/errors';
import { publicDirectoryApi, type DirectoryProfile } from '../../api/publicDirectoryApi';
import { queryKeys } from '../../api/queries';
import { useToast } from '../../hooks/useToast';

/**
 * The notice to a business that goes to its customers' homes while its store publishes the address of its main branch
 * (Fase 29A, ADR 0053 point 5). Nothing changes on its own: only the owner knows whether customers go there, so it says
 * where to mark the branch «Solo a domicilio», and «Ya lo revisé» puts it away for good, its line in Inicio with it,
 * and gives the keyboard back to `onDismissed`.
 */
export default function HomeVisitAddressNotice({ onDismissed }: { onDismissed?: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [saving, setSaving] = useState(false);

  const dismiss = async () => {
    setSaving(true);
    try {
      await publicDirectoryApi.dismissHomeVisitAddressNotice();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo guardar que ya lo revisaste.'));
      setSaving(false);
      return;
    }
    // Saved: the notice goes away now, not when the card is read again (a read that fails would leave it «Guardando…»).
    queryClient.setQueriesData<DirectoryProfile>({ queryKey: ['directory-profile'] }, (old) => old && { ...old, homeVisitAddressNotice: false });
    queryClient.setQueriesData<HomeVisitRequestsSettings>({ queryKey: ['home-visit-requests'] }, (old) => old && { ...old, addressNotice: false });
    // The notice and its button go away: the keyboard goes where its owner says.
    onDismissed?.();
    // What the server says next, and the line of Inicio with it.
    void queryClient.invalidateQueries({ queryKey: ['directory-profile'] });
    void queryClient.invalidateQueries({ queryKey: ['home-visit-requests'] });
    void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
  };

  return (
    <div className="alert alert-info py-2 small d-flex flex-wrap align-items-center gap-2">
      <span className="flex-grow-1">
        <i className="ri-map-pin-user-line me-1" aria-hidden="true"></i>
        Tu tienda muestra la dirección de tu sucursal principal. Si no atiendes en un local, márcala «Solo a domicilio» en{' '}
        <Link to="/branches">Sucursales</Link>.
      </span>
      <button type="button" className="btn btn-sm btn-light" disabled={saving} onClick={() => void dismiss()}>
        {saving ? 'Guardando…' : 'Ya lo revisé'}
      </button>
    </div>
  );
}
