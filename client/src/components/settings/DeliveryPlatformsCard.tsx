import { useId, useState } from 'react';
import { getApiErrorCode, getApiErrorMessage } from '../../api/errors';
import { useDeliveryPlatforms, useSaveDeliveryPlatform } from '../../api/queries';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { getAccess } from '../../utils/access';
import DeliveryPlatformForm from './DeliveryPlatformForm';
import {
  bodyOf,
  describeChange,
  describeExample,
  describeTerms,
  draftOf,
  draftProblem,
  platformName,
  savedMessage,
  type DeliveryPlatformDraft,
  type DraftProblem,
  type PlatformSetting,
} from './deliveryPlatformTerms';

interface Editing {
  /** The platform as it was read, with its revision. */
  platform: PlatformSetting;
  draft: DeliveryPlatformDraft;
}

/**
 * Plataformas de reparto in Mi negocio (owners and admins): Rappi, Uber Eats and DiDi Food, each with whether Vender
 * registers its orders and the terms of its contract. The server keeps them with a revision, works out the example of
 * each one and copies the terms into every order registered after a change; the ones already registered never change.
 */
export default function DeliveryPlatformsCard() {
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  // Whoever may open Catálogo is sent to the prices of each app from the terms.
  const access = getAccess(user, capabilities);
  const platforms = useDeliveryPlatforms(!!tenant);
  const save = useSaveDeliveryPlatform();
  const { confirm } = useConfirm();
  const toast = useToast();
  const titleId = useId();
  const [editing, setEditing] = useState<Editing | null>(null);
  const [problem, setProblem] = useState<DraftProblem | null>(null);
  if (!tenant) return null;

  const rows = platforms.data?.platforms;

  const open = (platform: PlatformSetting) => {
    setEditing({ platform, draft: draftOf(platform) });
    setProblem(null);
  };

  const submit = async () => {
    if (!editing) return;
    const { platform, draft } = editing;
    const name = platformName(platform.channel);
    const found = draftProblem(draft);
    if (found) {
      setProblem(found);
      return;
    }
    if (platform.isActive && !draft.isActive) {
      const accepted = await confirm({
        title: `¿Desactivar ${name}?`,
        message: `Ya no podrás registrar pedidos de ${name} en Vender. Los que ya registraste no cambian.`,
        confirmLabel: 'Desactivar',
        cancelLabel: 'Cancelar',
        danger: true,
      });
      if (!accepted) return;
    }
    setProblem(null);
    try {
      await save.mutateAsync({ channel: platform.channel, body: bodyOf(platform, draft) });
      setEditing(null);
      toast.success(savedMessage(platform, draft));
    } catch (err: unknown) {
      if (getApiErrorCode(err) === 'concurrency_conflict') {
        // Someone else saved it meanwhile: the list is read again and the form closes, to start from what is there now.
        void platforms.refetch();
        setEditing(null);
        toast.error(`Alguien más cambió ${name} al mismo tiempo. Revisa los datos y guarda otra vez.`);
        return;
      }
      setProblem({ message: getApiErrorMessage(err, `No se pudo guardar ${name}.`) });
    }
  };

  return (
    <section className="card" aria-labelledby={titleId}>
      <div className="card-body">
        <h4 id={titleId} className="header-title mt-0 mb-1">Plataformas de reparto</h4>
        <p className="text-muted small mb-3">
          Activa las apps por las que recibes pedidos para registrarlos en Vender con su folio. TienditApp calcula con estos
          datos la comisión, las retenciones y lo que te debe depositar cada plataforma.
        </p>

        {platforms.isPending ? (
          <p className="text-muted small mb-0" aria-busy="true">Cargando…</p>
        ) : platforms.isError || !rows ? (
          <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2 mb-0" role="alert">
            <span className="me-auto">No se pudieron cargar tus plataformas de reparto.</span>
            <button type="button" className="btn btn-sm btn-light" onClick={() => void platforms.refetch()}>Reintentar</button>
          </div>
        ) : (
          <ul className="list-group list-group-flush">
            {rows.map((platform) => {
              const name = platformName(platform.channel);
              const example = describeExample(platform);
              const change = describeChange(platform);
              return (
                <li key={platform.channel} className="list-group-item px-0 d-flex flex-wrap align-items-start justify-content-between gap-2">
                  <div className="flex-grow-1 text-break">
                    <div className="d-flex flex-wrap align-items-center gap-2">
                      <span className="fw-semibold">{name}</span>
                      {!platform.isConfigured ? (
                        <span className="small text-muted">Sin configurar</span>
                      ) : platform.isActive ? (
                        <span className="ta-chip ta-chip-ready">Activa</span>
                      ) : (
                        <span className="ta-chip ta-chip-done">No activa</span>
                      )}
                    </div>
                    {platform.isConfigured && <div className="small">{describeTerms(platform)}</div>}
                    {example && <div className="small text-muted">{example}</div>}
                    {change && <div className="small text-muted">{change}</div>}
                  </div>
                  <button
                    type="button"
                    className="btn btn-light btn-sm"
                    aria-label={`${platform.isConfigured ? 'Editar' : 'Configurar'} ${name}`}
                    onClick={() => open(platform)}
                  >
                    {platform.isConfigured ? 'Editar' : 'Configurar'}
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>

      {editing && (
        <DeliveryPlatformForm
          platform={editing.platform}
          draft={editing.draft}
          problem={problem}
          saving={save.isPending}
          onChange={(draft) => setEditing({ ...editing, draft })}
          onCancel={() => setEditing(null)}
          onSave={() => void submit()}
          pricesPath={access.catalog ? '/platform-prices' : undefined}
        />
      )}
    </section>
  );
}
