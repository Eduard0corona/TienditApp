import { useCallback, useEffect, useMemo, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { aiAgentsApi } from '../../api/aiAgentsApi';
import type { AiAgentDto, CreateAiAgentRequest, UpdateAiAgentRequest } from '../../types';
import { tenantApi } from '../../api/tenantApi';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';

type AgentTab = 'General' | 'Mensajes' | 'Datos que pide' | 'Comportamiento';

const createDefaultAgentPayload = (name: string): CreateAiAgentRequest => ({
  name,
  role: 'Asesor comercial',
  personality: 'Profesional y resolutivo',
  language: 'Espanol',
  tone: 'Amigable',
  businessName: '',
  businessDescription: '',
  businessProductsAndServices: '',
  welcomeMessage: 'Hola, soy tu asistente virtual. Te ayudo con productos, servicios y órdenes.',
  fallbackMessage: 'No tengo esa información por ahora, pero puedo ayudarte con otra consulta.',
  closingMessage: 'Gracias por contactarnos. Quedo atento para ayudarte nuevamente.',
  askForName: true,
  askForPhone: true,
  askForEmail: false,
  askForAddress: false,
  customQuestions: '',
  model: 'gpt-4.1-mini',
  temperature: 0.4,
  maxTokens: 800,
  useConversationMemory: true,
  strictMode: false,
});

const toUpdateRequest = (agent: AiAgentDto): UpdateAiAgentRequest => ({
  id: agent.id,
  name: agent.name,
  role: agent.role,
  personality: agent.personality,
  language: agent.language,
  tone: agent.tone,
  businessName: agent.businessName || '',
  businessDescription: agent.businessDescription || '',
  businessProductsAndServices: agent.businessProductsAndServices || '',
  welcomeMessage: agent.welcomeMessage,
  fallbackMessage: agent.fallbackMessage,
  closingMessage: agent.closingMessage,
  askForName: agent.askForName,
  askForPhone: agent.askForPhone,
  askForEmail: agent.askForEmail,
  askForAddress: agent.askForAddress,
  customQuestions: agent.customQuestions || '',
  model: agent.model,
  temperature: agent.temperature,
  maxTokens: agent.maxTokens,
  useConversationMemory: agent.useConversationMemory,
  strictMode: agent.strictMode,
});

export default function AiAgentSettings() {
  const { confirm } = useConfirm();
  const [agents, setAgents] = useState<AiAgentDto[]>([]);
  const [selectedAgentId, setSelectedAgentId] = useState<string>('');
  const [activeTab, setActiveTab] = useState<AgentTab>('General');
  const [feedback, setFeedback] = useState<{ type: 'success' | 'danger'; text: string } | null>(null);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [saving, setSaving] = useState(false);
  const [togglingActive, setTogglingActive] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const selectedAgent = useMemo(
    () => agents.find((agent) => agent.id === selectedAgentId) ?? null,
    [agents, selectedAgentId],
  );

  const loadAgents = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await aiAgentsApi.getAll();
      setAgents(data);
      setSelectedAgentId((current) => {
        if (current && data.some((x) => x.id === current)) return current;
        return data[0]?.id || '';
      });
    } catch {
      setFeedback({ type: 'danger', text: 'No se pudieron cargar las respuestas automáticas.' });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadAgents();
  }, [loadAgents]);

  const updateSelectedAgent = (updater: (current: AiAgentDto) => AiAgentDto) => {
    setAgents((current) =>
      current.map((agent) => (agent.id === selectedAgentId ? updater(agent) : agent)),
    );
  };

  const addAgent = async () => {
    setCreating(true);
    setFeedback(null);

    try {
      let tenantInfo: { name?: string } | null = null;
      try {
        const { data } = await tenantApi.getMyTenant();
        tenantInfo = { name: data.name };
      } catch {
        // Creating an agent should still work even if tenant info cannot be preloaded.
      }

      const nextNumber = agents.length + 1;
      const payload = createDefaultAgentPayload(`Respuestas ${nextNumber}`);
      if (tenantInfo?.name) {
        payload.businessName = tenantInfo.name;
      }

      const { data } = await aiAgentsApi.create(payload);
      setAgents((current) => [...current, data].sort((a, b) => a.name.localeCompare(b.name)));
      setSelectedAgentId(data.id);
      setActiveTab('General');
      setFeedback({ type: 'success', text: 'Respuestas automáticas creadas.' });
    } catch (err: unknown) {
      setFeedback({ type: 'danger', text: getApiErrorMessage(err, 'No se pudieron crear las respuestas automáticas.') });
    } finally {
      setCreating(false);
    }
  };

  const deleteAgent = async () => {
    if (!selectedAgent) return;
    const accepted = await confirm({
      title: 'Eliminar respuestas automáticas',
      message: `Se eliminará “${selectedAgent.name}” y dejará de contestar mensajes.`,
      confirmLabel: 'Eliminar',
      danger: true,
    });
    if (!accepted) return;

    setDeleting(true);
    setFeedback(null);

    try {
      await aiAgentsApi.delete(selectedAgent.id);

      const nextAgents = agents.filter((x) => x.id !== selectedAgent.id);
      setAgents(nextAgents);
      setSelectedAgentId(nextAgents[0]?.id || '');

      setFeedback({ type: 'success', text: 'Respuestas automáticas eliminadas.' });
    } catch (err: unknown) {
      setFeedback({ type: 'danger', text: getApiErrorMessage(err, 'No se pudieron eliminar las respuestas automáticas.') });
    } finally {
      setDeleting(false);
    }
  };

  const saveAgent = async () => {
    if (!selectedAgent) return;

    if (!selectedAgent.name.trim()) {
      setFeedback({ type: 'danger', text: 'Escribe un nombre.' });
      return;
    }

    if (!selectedAgent.role.trim()) {
      setFeedback({ type: 'danger', text: 'Escribe para qué las usas.' });
      return;
    }

    setSaving(true);
    setFeedback(null);

    try {
      const { data } = await aiAgentsApi.update(selectedAgent.id, toUpdateRequest(selectedAgent));
      setAgents((current) => current.map((agent) => (agent.id === data.id ? data : agent)));
      setFeedback({ type: 'success', text: 'Respuestas automáticas guardadas.' });
    } catch (err: unknown) {
      setFeedback({ type: 'danger', text: getApiErrorMessage(err, 'No se pudieron guardar las respuestas automáticas.') });
    } finally {
      setSaving(false);
    }
  };

  const toggleActive = async () => {
    if (!selectedAgent) return;

    setTogglingActive(true);
    setFeedback(null);
    const nextStatus = !selectedAgent.isActive;

    try {
      await aiAgentsApi.setActive(selectedAgent.id, nextStatus);
      setAgents((current) =>
        current.map((agent) =>
          agent.id === selectedAgent.id ? { ...agent, isActive: nextStatus } : agent,
        ),
      );
      setFeedback({
        type: 'success',
        text: nextStatus ? 'Respuestas automáticas activadas.' : 'Respuestas automáticas desactivadas.',
      });
    } catch (err: unknown) {
      setFeedback({ type: 'danger', text: getApiErrorMessage(err, 'No se pudo cambiar el estado.') });
    } finally {
      setTogglingActive(false);
    }
  };

  if (loading) {
    return (
      <>
        <PageTitle title="Respuestas automáticas" subtitle="Configuración" />
        <div className="text-center py-5">
          <div className="spinner-border text-primary" role="status">
            <span className="visually-hidden">Cargando...</span>
          </div>
        </div>
      </>
    );
  }

  return (
    <>
      <PageTitle title="Respuestas automáticas" subtitle="Configuración" />

      {feedback && (
        <div className={`alert alert-${feedback.type} alert-dismissible fade show`}>
          {feedback.text}
          <button type="button" className="btn-close" onClick={() => setFeedback(null)}></button>
        </div>
      )}

      <div className="row">
        <div className="col-xl-3">
          <div className="card">
            <div className="card-body">
              <div className="d-flex justify-content-between align-items-center mb-3">
                <h4 className="header-title mb-0">Configuraciones</h4>
                <button type="button" className="btn btn-sm btn-primary" onClick={addAgent} disabled={creating}>
                  <i className="ri-add-line me-1"></i>{creating ? 'Creando...' : 'Nuevo'}
                </button>
              </div>

              {agents.length === 0 ? (
                <div className="text-muted small">
                  Todavía no hay respuestas automáticas.
                </div>
              ) : (
                <div className="list-group">
                  {agents.map((agent) => (
                    <button
                      key={agent.id}
                      type="button"
                      className={`list-group-item list-group-item-action ${
                        agent.id === selectedAgentId ? 'active' : ''
                      }`}
                      onClick={() => {
                        setSelectedAgentId(agent.id);
                        setFeedback(null);
                      }}
                    >
                      <div className="d-flex justify-content-between align-items-start gap-2">
                        <div>
                          <div className="fw-semibold">{agent.name || 'Sin nombre'}</div>
                          <small className={agent.id === selectedAgentId ? 'text-white' : 'text-muted'}>
                            {agent.role || 'Sin rol'}
                          </small>
                        </div>
                        <span className={`badge ${agent.isActive ? 'bg-success' : 'bg-secondary'}`}>
                          {agent.isActive ? 'Activo' : 'Inactivo'}
                        </span>
                      </div>
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="col-xl-9">
          {!selectedAgent ? (
            <div className="card">
              <div className="card-body text-center py-5">
                <h5 className="mb-2">Sin respuestas automáticas</h5>
                <p className="text-muted mb-3">Crea las respuestas que tu negocio manda por WhatsApp cuando nadie del equipo contesta.</p>
                <button type="button" className="btn btn-primary" onClick={addAgent} disabled={creating}>
                  <i className="ri-add-line me-1"></i>Crear respuestas
                </button>
              </div>
            </div>
          ) : (
            <div className="card">
              <div className="card-body">
                <div className="alert alert-light border small">
                  Reconocen palabras completas (saludos, precios, menú, horario y ubicación) y contestan con tus textos;
                  no usan inteligencia artificial. Se pausan solas 12 horas cuando alguien del equipo escribe en el chat
                  y nunca contestan fotos ni notas de voz.
                </div>
                <ul className="nav nav-tabs nav-bordered mb-3">
                  {(['General', 'Mensajes', 'Datos que pide', 'Comportamiento'] as AgentTab[]).map((tab) => (
                    <li className="nav-item" key={tab}>
                      <button
                        type="button"
                        className={`nav-link ${activeTab === tab ? 'active' : ''}`}
                        onClick={() => setActiveTab(tab)}
                      >
                        {tab}
                      </button>
                    </li>
                  ))}
                </ul>

                {activeTab === 'General' && (
                  <div>
                    <h5 className="mb-3">Datos</h5>
                    <div className="row">
                      <div className="col-md-6">
                        <div className="mb-3">
                          <label htmlFor="aiagentsettings-nombre" className="form-label">Nombre</label>
                          <input id="aiagentsettings-nombre"
                            type="text"
                            className="form-control"
                            value={selectedAgent.name}
                            onChange={(e) =>
                              updateSelectedAgent((agent) => ({
                                ...agent,
                                name: e.target.value,
                              }))
                            }
                          />
                        </div>
                      </div>
                      <div className="col-md-6">
                        <div className="mb-3">
                          <label htmlFor="aiagentsettings-para-que-la-usas" className="form-label">Para qué la usas</label>
                          <input id="aiagentsettings-para-que-la-usas"
                            type="text"
                            className="form-control"
                            value={selectedAgent.role}
                            onChange={(e) =>
                              updateSelectedAgent((agent) => ({
                                ...agent,
                                role: e.target.value,
                              }))
                            }
                          />
                        </div>
                      </div>
                    </div>

                    <h5 className="mb-3 mt-2">Información del negocio</h5>
                    <div className="mb-3">
                      <label htmlFor="aiagentsettings-nombre-de-la-empresa" className="form-label">Nombre de la empresa</label>
                      <input id="aiagentsettings-nombre-de-la-empresa"
                        type="text"
                        className="form-control"
                        value={selectedAgent.businessName || ''}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            businessName: e.target.value,
                          }))
                        }
                      />
                    </div>
                    <div className="mb-3">
                      <label htmlFor="aiagentsettings-descripcion-del-negocio" className="form-label">Descripción del negocio</label>
                      <textarea id="aiagentsettings-descripcion-del-negocio"
                        className="form-control"
                        rows={3}
                        value={selectedAgent.businessDescription || ''}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            businessDescription: e.target.value,
                          }))
                        }
                      />
                    </div>
                    <div className="mb-0">
                      <label htmlFor="aiagentsettings-productos-y-servicios" className="form-label">Productos y servicios</label>
                      <textarea id="aiagentsettings-productos-y-servicios"
                        className="form-control"
                        rows={4}
                        placeholder="Ejemplo: Tacos al pastor, combos familiares, asesoria legal, mantenimiento preventivo..."
                        value={selectedAgent.businessProductsAndServices || ''}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            businessProductsAndServices: e.target.value,
                          }))
                        }
                      />
                    </div>
                  </div>
                )}

                {activeTab === 'Mensajes' && (
                  <div>
                    <h5 className="mb-3">Mensajes</h5>
                    <div className="mb-3">
                      <label htmlFor="aiagentsettings-mensaje-de-bienvenida" className="form-label">Mensaje de bienvenida</label>
                      <textarea id="aiagentsettings-mensaje-de-bienvenida"
                        className="form-control"
                        rows={3}
                        value={selectedAgent.welcomeMessage}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            welcomeMessage: e.target.value,
                          }))
                        }
                      />
                    </div>
                    <div className="mb-3">
                      <label htmlFor="aiagentsettings-mensaje-de-respaldo" className="form-label">Mensaje de respaldo</label>
                      <textarea id="aiagentsettings-mensaje-de-respaldo"
                        className="form-control"
                        rows={3}
                        value={selectedAgent.fallbackMessage}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            fallbackMessage: e.target.value,
                          }))
                        }
                      />
                    </div>
                    <div className="mb-0">
                      <label htmlFor="aiagentsettings-mensaje-de-cierre" className="form-label">Mensaje de cierre</label>
                      <textarea id="aiagentsettings-mensaje-de-cierre"
                        className="form-control"
                        rows={3}
                        value={selectedAgent.closingMessage}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            closingMessage: e.target.value,
                          }))
                        }
                      />
                    </div>
                  </div>
                )}

                {activeTab === 'Datos que pide' && (
                  <div>
                    <h5 className="mb-1">Datos que pide al saludar</h5>
                    <p className="text-muted small">Se piden una sola vez, junto con el mensaje de bienvenida.</p>
                    <div className="form-check mb-2">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="ask-name"
                        checked={selectedAgent.askForName}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            askForName: e.target.checked,
                          }))
                        }
                      />
                      <label className="form-check-label" htmlFor="ask-name">Nombre (si WhatsApp no lo da)</label>
                    </div>
                    <div className="form-check mb-2">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="ask-email"
                        checked={selectedAgent.askForEmail}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            askForEmail: e.target.checked,
                          }))
                        }
                      />
                      <label className="form-check-label" htmlFor="ask-email">Correo</label>
                    </div>
                    <div className="form-check mb-3">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="ask-address"
                        checked={selectedAgent.askForAddress}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            askForAddress: e.target.checked,
                          }))
                        }
                      />
                      <label className="form-check-label" htmlFor="ask-address">Dirección de entrega</label>
                    </div>

                  </div>
                )}

                {activeTab === 'Comportamiento' && (
                  <div>
                    <h5 className="mb-3">Cuando no reconoce la pregunta</h5>
                    <div className="form-check mb-1">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="strict-mode"
                        checked={selectedAgent.strictMode}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            strictMode: e.target.checked,
                          }))
                        }
                      />
                      <label className="form-check-label" htmlFor="strict-mode">Contestar solo el mensaje de respaldo</label>
                    </div>
                    <p className="text-muted small mb-0">
                      Si lo desactivas, el mensaje de respaldo va seguido de la descripción del negocio.
                    </p>
                  </div>
                )}

                <div className="mt-4 d-flex justify-content-between">
                  <div className="d-flex gap-2">
                    <button
                      type="button"
                      className={`btn ${selectedAgent.isActive ? 'btn-outline-danger' : 'btn-outline-success'}`}
                      onClick={toggleActive}
                      disabled={togglingActive || deleting}
                    >
                      {togglingActive
                        ? 'Actualizando...'
                        : selectedAgent.isActive
                          ? 'Desactivar'
                          : 'Activar'}
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline-danger"
                      onClick={deleteAgent}
                      disabled={deleting || saving || togglingActive}
                    >
                      {deleting ? 'Eliminando...' : 'Eliminar'}
                    </button>
                  </div>
                  <button type="button" className="btn btn-primary" onClick={saveAgent} disabled={saving || deleting}>
                    <i className="ri-save-line me-1"></i>{saving ? 'Guardando...' : 'Guardar configuración'}
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </>
  );
}
