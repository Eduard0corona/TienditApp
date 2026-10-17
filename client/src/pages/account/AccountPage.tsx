import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi, MAX_ACCOUNT_ADDRESSES, type AccountAddress, type AccountAddressInput } from '../../api/accountApi';
import { getApiErrorMessage } from '../../api/errors';
import PageTitle from '../../components/common/PageTitle';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { describeAccountAddress } from '../../utils/accountAddresses';
import {
  ADDRESS_LABEL_MAX_LENGTH,
  ADDRESS_LINE_MAX_LENGTH,
  REFERENCES_MAX_LENGTH,
} from '../../utils/homeDelivery';
import { localPhone } from '../../utils/phones';
import { isPostalCode } from '../../utils/postalCodes';

const EMPTY: AccountAddressInput = { label: '', postalCode: '', addressLine: '', references: '' };

/** Adding an address (no id) or changing one. */
function AddressDialog({ address, onClose }: { address: AccountAddress | 'new' | null; onClose: () => void }) {
  const titleId = useId();
  const ids = useId();
  const toast = useToast();
  const queryClient = useQueryClient();
  const editing = address && address !== 'new' ? address : null;
  const [form, setForm] = useState<AccountAddressInput>(editing
    ? { label: editing.label, postalCode: editing.postalCode, addressLine: editing.addressLine, references: editing.references ?? '' }
    : EMPTY);
  const [shownFor, setShownFor] = useState(address);
  // Opening the dialog for another address starts from what it keeps.
  if (address !== shownFor) {
    setShownFor(address);
    setForm(editing
      ? { label: editing.label, postalCode: editing.postalCode, addressLine: editing.addressLine, references: editing.references ?? '' }
      : EMPTY);
  }
  const save = useMutation({
    mutationFn: (body: AccountAddressInput) => (editing ? accountApi.updateAddress(editing.id, body) : accountApi.saveAddress(body)),
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['me', 'addresses'] });
      toast.success(`${saved.label}: guardada.`);
      onClose();
    },
  });
  const postalCodeProblem = form.postalCode && !isPostalCode(form.postalCode) ? 'El código postal lleva 5 dígitos.' : '';
  const ready = isPostalCode(form.postalCode) && form.addressLine.trim() !== '';

  return (
    <Modal show={address !== null} onHide={onClose} centered fullscreen="sm-down" aria-labelledby={titleId}>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          if (!ready || save.isPending) return;
          save.mutate({
            label: form.label?.trim() || null,
            postalCode: form.postalCode,
            addressLine: form.addressLine.trim(),
            references: form.references?.trim() || null,
          });
        }}
      >
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">{editing ? `Cambiar ${editing.label}` : 'Agregar una dirección'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="row g-2">
            <div className="col-12">
              <label className="form-label small mb-1" htmlFor={`${ids}-label`}>Nombre (opcional)</label>
              <input
                id={`${ids}-label`}
                className="form-control"
                maxLength={ADDRESS_LABEL_MAX_LENGTH}
                placeholder="Casa, Oficina…"
                value={form.label ?? ''}
                onChange={(event) => setForm({ ...form, label: event.target.value })}
              />
            </div>
            <div className="col-6 col-sm-4">
              <label className="form-label small mb-1" htmlFor={`${ids}-postal-code`}>Código postal</label>
              <input
                id={`${ids}-postal-code`}
                className={`form-control ${postalCodeProblem ? 'is-invalid' : ''}`}
                required
                inputMode="numeric"
                autoComplete="postal-code"
                maxLength={5}
                pattern="\d{5}"
                value={form.postalCode}
                onChange={(event) => setForm({ ...form, postalCode: event.target.value.replace(/\D/g, '') })}
                aria-describedby={`${ids}-postal-code-problem`}
              />
              <div id={`${ids}-postal-code-problem`} className="invalid-feedback">{postalCodeProblem}</div>
            </div>
            <div className="col-12">
              <label className="form-label small mb-1" htmlFor={`${ids}-address`}>Dirección</label>
              <input
                id={`${ids}-address`}
                className="form-control"
                required
                maxLength={ADDRESS_LINE_MAX_LENGTH}
                autoComplete="street-address"
                placeholder="Calle, número y colonia"
                value={form.addressLine}
                onChange={(event) => setForm({ ...form, addressLine: event.target.value })}
              />
            </div>
            <div className="col-12">
              <label className="form-label small mb-1" htmlFor={`${ids}-references`}>Referencias (opcional)</label>
              <input
                id={`${ids}-references`}
                className="form-control"
                maxLength={REFERENCES_MAX_LENGTH}
                placeholder="Ej. portón verde, entre qué calles"
                value={form.references ?? ''}
                onChange={(event) => setForm({ ...form, references: event.target.value })}
              />
            </div>
          </div>
          {save.isError && <div className="alert alert-danger mt-3 mb-0">{getApiErrorMessage(save.error, 'No se pudo guardar la dirección.')}</div>}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={!ready || save.isPending}>
            {save.isPending ? 'Guardando…' : 'Guardar'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}

/**
 * Cuenta (/cuenta, Fase 24B): the customer account's details and the addresses it keeps for its orders. The store
 * offers them when an order goes to an address; here they are added, changed and deleted. The password and the phone
 * change in the profile.
 */
export default function AccountPage() {
  const { user } = useAuth();
  const toast = useToast();
  const { confirm } = useConfirm();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<AccountAddress | 'new' | null>(null);
  const addresses = useQuery({
    queryKey: ['me', 'addresses'],
    queryFn: ({ signal }) => accountApi.getAddresses(signal),
  });
  const remove = useMutation({
    mutationFn: (address: AccountAddress) => accountApi.deleteAddress(address.id),
    onSuccess: async (_, address) => {
      await queryClient.invalidateQueries({ queryKey: ['me', 'addresses'] });
      toast.success(`${address.label}: borrada.`);
    },
    onError: (error) => toast.error(getApiErrorMessage(error, 'No se pudo borrar la dirección.')),
  });
  const kept = addresses.data ?? [];
  const full = kept.length >= MAX_ACCOUNT_ADDRESSES;

  const askToRemove = async (address: AccountAddress) => {
    const ok = await confirm({
      title: 'Borrar dirección',
      message: `${address.label} (${describeAccountAddress(address)}) deja de aparecer al pedir. Los pedidos que ya enviaste ahí no cambian.`,
      confirmLabel: 'Borrar',
      danger: true,
    });
    if (ok) remove.mutate(address);
  };

  // Inside the customer's frame (CustomerLayout), which has no margins of its own.
  return (
    <div className="cl-page">
      <PageTitle title="Mi cuenta" />

      <section className="card mb-3" aria-labelledby="account-details">
        <div className="card-body">
          <h2 id="account-details" className="h6">Tus datos</h2>
          <dl className="row small mb-2">
            <dt className="col-sm-3 text-muted fw-normal">Nombre</dt>
            <dd className="col-sm-9">{user ? `${user.firstName} ${user.lastName}`.trim() : ''}</dd>
            <dt className="col-sm-3 text-muted fw-normal">Correo</dt>
            <dd className="col-sm-9">{user?.email}</dd>
            <dt className="col-sm-3 text-muted fw-normal">Teléfono</dt>
            <dd className="col-sm-9">{localPhone(user?.phoneNumber) || 'No registrado'}</dd>
          </dl>
          <p className="small text-muted mb-1">Al pedir en un negocio ya van escritos; puedes cambiarlos en cada pedido.</p>
          <Link className="small" to="/profile">Cambiar tu contraseña o tu teléfono</Link>
        </div>
      </section>

      <section className="card" aria-labelledby="account-addresses">
        <div className="card-body">
          <div className="d-flex flex-wrap align-items-center gap-2 mb-2">
            <h2 id="account-addresses" className="h6 mb-0 me-auto">Tus direcciones</h2>
            <button type="button" className="btn btn-sm btn-primary" disabled={full} onClick={() => setEditing('new')}>
              <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar dirección
            </button>
          </div>
          {full && <p className="small text-muted">Guardas hasta {MAX_ACCOUNT_ADDRESSES} direcciones: borra una para agregar otra.</p>}
          {addresses.isLoading && <p className="text-muted small mb-0" aria-busy="true">Cargando tus direcciones…</p>}
          {addresses.isError && (
            <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2 mb-0" role="alert">
              <span className="me-auto">{getApiErrorMessage(addresses.error, 'No se pudieron cargar tus direcciones.')}</span>
              <button type="button" className="btn btn-sm btn-light" onClick={() => void addresses.refetch()}>Reintentar</button>
            </div>
          )}
          {addresses.isSuccess && kept.length === 0 && (
            <p className="text-muted small mb-0">
              Todavía no guardas direcciones. Agrega una aquí, o guárdala al pedir a domicilio.
            </p>
          )}
          {kept.length > 0 && (
            <ul className="list-unstyled mb-0">
              {kept.map((address) => (
                <li key={address.id} className="d-flex flex-wrap align-items-start gap-2 py-2 border-top">
                  <div className="flex-grow-1 min-w-0">
                    <div className="fw-semibold">{address.label}</div>
                    <div className="small">{describeAccountAddress(address)}</div>
                    {address.references && <div className="small text-muted">{address.references}</div>}
                  </div>
                  <button
                    type="button"
                    className="btn btn-sm btn-light"
                    onClick={() => setEditing(address)}
                    aria-label={`Cambiar ${address.label}`}
                  >
                    Cambiar
                  </button>
                  <button
                    type="button"
                    className="btn btn-sm btn-light"
                    disabled={remove.isPending}
                    onClick={() => void askToRemove(address)}
                    aria-label={`Borrar ${address.label}`}
                  >
                    Borrar
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>

      <AddressDialog address={editing} onClose={() => setEditing(null)} />
    </div>
  );
}
