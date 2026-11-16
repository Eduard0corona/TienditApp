import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { assistantsApi, type MyAssistantConnection } from '../../api/assistantsApi';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { formatDate } from '../../utils/dates';

/**
 * The AI assistants connected to the customer account (Fase 32E, ADR 0097), in Mi cuenta: who, the site its permission
 * returned to, since when and when it last read the account; disconnecting one stops it at its next request.
 */
export default function ConnectedAssistantsCard() {
  const toast = useToast();
  const { confirm } = useConfirm();
  const queryClient = useQueryClient();
  const assistants = useQuery({
    queryKey: ['me', 'assistants'],
    queryFn: ({ signal }) => assistantsApi.getMine(signal),
  });
  const disconnect = useMutation({
    mutationFn: (assistant: MyAssistantConnection) => assistantsApi.disconnect(assistant.id),
    onSuccess: async (_, assistant) => {
      await queryClient.invalidateQueries({ queryKey: ['me', 'assistants'] });
      toast.success(`${nameOf(assistant)}: desconectado.`);
    },
    onError: (error) => toast.error(getApiErrorMessage(error, 'No se pudo desconectar el asistente.')),
  });
  const connected = assistants.data ?? [];

  const askToDisconnect = async (assistant: MyAssistantConnection) => {
    const ok = await confirm({
      title: 'Desconectar asistente',
      message: `${nameOf(assistant)} deja de ver tu cuenta. Los pedidos que ya preparó siguen en sus enlaces hasta que venzan.`,
      confirmLabel: 'Desconectar',
      danger: true,
    });
    if (ok) disconnect.mutate(assistant);
  };

  return (
    <section className="card mt-3" aria-labelledby="account-assistants">
      <div className="card-body">
        <h2 id="account-assistants" className="h6">Asistentes conectados</h2>
        <p className="small text-muted">
          Los asistentes de IA a los que diste permiso ven tus direcciones, tus pedidos y tus favoritos, y te preparan pedidos
          que tú envías. <Link to="/asistentes">Cómo conectar uno</Link>
        </p>
        {assistants.isLoading && <p className="text-muted small mb-0" aria-busy="true">Cargando tus asistentes…</p>}
        {assistants.isError && (
          <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2 mb-0" role="alert">
            <span className="me-auto">{getApiErrorMessage(assistants.error, 'No se pudieron cargar tus asistentes.')}</span>
            <button type="button" className="btn btn-sm btn-light" onClick={() => void assistants.refetch()}>Reintentar</button>
          </div>
        )}
        {assistants.isSuccess && connected.length === 0 && (
          <p className="text-muted small mb-0">No tienes asistentes conectados.</p>
        )}
        {connected.length > 0 && (
          <ul className="list-unstyled mb-0">
            {connected.map((assistant) => (
              <li key={assistant.id} className="d-flex flex-column flex-sm-row align-items-sm-start gap-2 py-2 border-top">
                <div className="flex-grow-1 min-w-0">
                  <div className="fw-semibold">{nameOf(assistant)}</div>
                  <div className="small text-muted">
                    {assistant.site} · conectado el {formatDate(assistant.grantedAt)}
                    {assistant.lastUsedAt ? ` · lo usó el ${formatDate(assistant.lastUsedAt)}` : ' · todavía no lo usa'}
                  </div>
                </div>
                <div>
                  <button
                    type="button"
                    className="btn btn-sm btn-light"
                    disabled={disconnect.isPending}
                    onClick={() => void askToDisconnect(assistant)}
                    aria-label={`Desconectar ${nameOf(assistant)}`}
                  >
                    Desconectar
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}

function nameOf(assistant: MyAssistantConnection) {
  return assistant.name ?? 'Asistente sin nombre';
}
