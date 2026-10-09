import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import { useToast } from '../../hooks/useToast';

interface CustomerNotesAndAddressesProps {
  customerId?: string | null;
  notes?: string | null;
  addresses: Schemas['CustomerAddressDto'][];
  /** To save a person from the chat as a customer when there is no record yet. */
  phone?: string | null;
  name?: string | null;
  canCreateCustomer: boolean;
}

interface AddressDraft {
  label: string;
  addressLine: string;
  references: string;
}

const EMPTY_ADDRESS: AddressDraft = { label: '', addressLine: '', references: '' };

/** What the team remembers about a customer and where they receive their orders. */
export default function CustomerNotesAndAddresses({
  customerId,
  notes,
  addresses,
  phone,
  name,
  canCreateCustomer,
}: CustomerNotesAndAddressesProps) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [notesDraft, setNotesDraft] = useState(notes ?? '');
  const [addressDraft, setAddressDraft] = useState<AddressDraft | null>(null);
  const [busy, setBusy] = useState(false);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['customers'] });

  const run = async (action: () => Promise<unknown>, success: string, failure: string) => {
    setBusy(true);
    try {
      await action();
      await refresh();
      toast.success(success);
      return true;
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, failure));
      return false;
    } finally {
      setBusy(false);
    }
  };

  if (!customerId) {
    return (
      <div className="border rounded p-2 mb-3 small">
        <p className="text-muted mb-2">Guárdalo como cliente para anotar sus preferencias y direcciones de entrega.</p>
        {canCreateCustomer && phone && (
          <button
            type="button"
            className="btn btn-sm btn-light"
            disabled={busy}
            onClick={() => run(
              () => apiRequest('post', '/api/v1/customers', { body: { name: name?.trim() || 'Cliente', phone } }),
              'Cliente guardado.',
              'No se pudo guardar el cliente.',
            )}
          >
            <i className="ri-user-add-line me-1" aria-hidden="true"></i>Guardar como cliente
          </button>
        )}
      </div>
    );
  }

  const saveNotes = () => run(
    () => apiRequest('put', '/api/v1/customers/{id}/notes', { path: { id: customerId }, body: { notes: notesDraft } }),
    'Notas guardadas.',
    'No se pudieron guardar las notas.',
  );

  const saveAddress = async () => {
    if (!addressDraft) return;
    const saved = await run(
      () => apiRequest('post', '/api/v1/customers/{id}/addresses', {
        path: { id: customerId },
        body: {
          label: addressDraft.label.trim() || null,
          addressLine: addressDraft.addressLine.trim(),
          references: addressDraft.references.trim() || null,
          isDefault: false,
        },
      }),
      'Dirección guardada.',
      'No se pudo guardar la dirección.',
    );
    if (saved) setAddressDraft(null);
  };

  const makeDefault = (address: Schemas['CustomerAddressDto']) => run(
    () => apiRequest('put', '/api/v1/customers/{id}/addresses/{addressId}', {
      path: { id: customerId, addressId: address.id },
      body: { label: address.label, addressLine: address.addressLine, references: address.references ?? null, isDefault: true },
    }),
    `“${address.label}” es ahora la dirección principal.`,
    'No se pudo cambiar la dirección principal.',
  );

  const remove = (address: Schemas['CustomerAddressDto']) => run(
    () => apiRequest('delete', '/api/v1/customers/{id}/addresses/{addressId}', { path: { id: customerId, addressId: address.id } }),
    'Dirección quitada.',
    'No se pudo quitar la dirección.',
  );

  return (
    <div className="mb-3">
      <label className="fw-semibold small mb-1" htmlFor={`customer-notes-${customerId}`}>Notas</label>
      <textarea
        id={`customer-notes-${customerId}`}
        className="form-control form-control-sm mb-1"
        rows={2}
        maxLength={2000}
        placeholder="Preferencias, alergias, cómo paga…"
        value={notesDraft}
        onChange={(event) => setNotesDraft(event.target.value)}
      />
      {notesDraft !== (notes ?? '') && (
        <div className="d-flex justify-content-end gap-2 mb-2">
          <button type="button" className="btn btn-sm btn-light" onClick={() => setNotesDraft(notes ?? '')} disabled={busy}>
            Descartar
          </button>
          <button type="button" className="btn btn-sm btn-primary" onClick={saveNotes} disabled={busy}>
            Guardar notas
          </button>
        </div>
      )}

      <div className="d-flex justify-content-between align-items-center mt-2 mb-1">
        <span className="fw-semibold small">Direcciones de entrega</span>
        {!addressDraft && (
          <button type="button" className="btn btn-link btn-sm p-0" onClick={() => setAddressDraft(EMPTY_ADDRESS)}>
            <i className="ri-add-line" aria-hidden="true"></i> Agregar
          </button>
        )}
      </div>
      {addresses.length === 0 && !addressDraft && <p className="small text-muted mb-0">Sin direcciones guardadas.</p>}
      <ul className="list-unstyled small mb-1">
        {addresses.map((address) => (
          <li key={address.id} className="border rounded p-2 mb-1">
            <div className="d-flex justify-content-between gap-2">
              <span className="fw-semibold">
                {address.label}
                {address.isDefault && <span className="badge bg-success-subtle text-success ms-1">Principal</span>}
              </span>
              <span className="text-nowrap">
                {!address.isDefault && (
                  <button type="button" className="btn btn-link btn-sm p-0 me-2" onClick={() => makeDefault(address)} disabled={busy}>
                    Principal
                  </button>
                )}
                <button
                  type="button"
                  className="btn btn-link btn-sm p-0 text-danger"
                  onClick={() => remove(address)}
                  disabled={busy}
                  aria-label={`Quitar ${address.label}`}
                >
                  Quitar
                </button>
              </span>
            </div>
            <div>{address.addressLine}</div>
            {address.references && <div className="text-muted">{address.references}</div>}
          </li>
        ))}
      </ul>
      {addressDraft && (
        <div className="border rounded p-2">
          <input
            className="form-control form-control-sm mb-1"
            placeholder="Nombre (casa, oficina…)"
            maxLength={60}
            aria-label="Nombre de la dirección"
            value={addressDraft.label}
            onChange={(event) => setAddressDraft({ ...addressDraft, label: event.target.value })}
          />
          <input
            className="form-control form-control-sm mb-1"
            placeholder="Calle, número y colonia"
            maxLength={300}
            aria-label="Dirección"
            value={addressDraft.addressLine}
            onChange={(event) => setAddressDraft({ ...addressDraft, addressLine: event.target.value })}
          />
          <input
            className="form-control form-control-sm mb-2"
            placeholder="Referencias (entre calles, color de la casa…)"
            maxLength={500}
            aria-label="Referencias"
            value={addressDraft.references}
            onChange={(event) => setAddressDraft({ ...addressDraft, references: event.target.value })}
          />
          <div className="d-flex justify-content-end gap-2">
            <button type="button" className="btn btn-sm btn-light" onClick={() => setAddressDraft(null)} disabled={busy}>
              Cancelar
            </button>
            <button
              type="button"
              className="btn btn-sm btn-primary"
              onClick={saveAddress}
              disabled={busy || !addressDraft.addressLine.trim()}
            >
              Guardar dirección
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
