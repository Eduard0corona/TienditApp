import { useCallback, useEffect, useMemo, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { aiAgentsApi } from '../../api/aiAgentsApi';
import type { AiAgentDto, CreateAiAgentRequest, UpdateAiAgentRequest } from '../../types';
import { tenantApi } from '../../api/tenantApi';

type AgentTab = 'General' | 'Mensajes' | 'Captura de Datos' | 'Configuracion IA';

const createDefaultAgentPayload = (name: string): CreateAiAgentRequest => ({
  name,
  role: 'Asesor comercial',
  personality: 'Profesional y resolutivo',
  language: 'Espanol',
  tone: 'Amigable',
  businessName: '',
  businessDescription: '',
  businessProductsAndServices: '',
  welcomeMessage: 'Hola, soy tu asistente virtual. Te ayudo con productos, servicios y ordenes.',
  fallbackMessage: 'No tengo esa informacion por ahora, pero puedo ayudarte con otra consulta.',
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

const getApiError = (err: unknown, fallback: string): string =>
  (err as { response?: { data?: { error?: string; detail?: string } } })?.response?.data?.error ||
  (err as { response?: { data?: { error?: string; detail?: string } } })?.response?.data?.detail ||
  fallback;

export default function AiAgentSettings() {
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
      setFeedback({ type: 'danger', text: 'No se pudieron cargar los agentes IA.' });
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
      const payload = createDefaultAgentPayload(`Agente ${nextNumber}`);
      if (tenantInfo?.name) {
        payload.businessName = tenantInfo.name;
      }

      const { data } = await aiAgentsApi.create(payload);
      setAgents((current) => [...current, data].sort((a, b) => a.name.localeCompare(b.name)));
      setSelectedAgentId(data.id);
      setActiveTab('General');
      setFeedback({ type: 'success', text: 'Agente IA creado correctamente.' });
    } catch (err: unknown) {
      setFeedback({ type: 'danger', text: getApiError(err, 'No se pudo crear el agente IA.') });
    } finally {
      setCreating(false);
    }
  };

  const deleteAgent = async () => {
    if (!selectedAgent) return;
    if (!window.confirm(`Deseas eliminar el agente "${selectedAgent.name}"?`)) return;

    setDeleting(true);
    setFeedback(null);

    try {
      await aiAgentsApi.delete(selectedAgent.id);

      const nextAgents = agents.filter((x) => x.id !== selectedAgent.id);
      setAgents(nextAgents);
      setSelectedAgentId(nextAgents[0]?.id || '');

      setFeedback({ type: 'success', text: 'Agente eliminado correctamente.' });
    } catch (err: unknown) {
      setFeedback({ type: 'danger', text: getApiError(err, 'No se pudo eliminar el agente IA.') });
    } finally {
      setDeleting(false);
    }
  };

  const saveAgent = async () => {
    if (!selectedAgent) return;

    if (!selectedAgent.name.trim()) {
      setFeedback({ type: 'danger', text: 'El nombre del agente es obligatorio.' });
      return;
    }

    if (!selectedAgent.role.trim()) {
      setFeedback({ type: 'danger', text: 'El rol del agente es obligatorio.' });
      return;
    }

    setSaving(true);
    setFeedback(null);

    try {
      const { data } = await aiAgentsApi.update(selectedAgent.id, toUpdateRequest(selectedAgent));
      setAgents((current) => current.map((agent) => (agent.id === data.id ? data : agent)));
      setFeedback({ type: 'success', text: 'Configuracion del agente guardada correctamente.' });
    } catch (err: unknown) {
      setFeedback({ type: 'danger', text: getApiError(err, 'No se pudo guardar el agente IA.') });
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
        text: nextStatus ? 'Agente activado correctamente.' : 'Agente desactivado correctamente.',
      });
    } catch (err: unknown) {
      setFeedback({ type: 'danger', text: getApiError(err, 'No se pudo actualizar el estado del agente.') });
    } finally {
      setTogglingActive(false);
    }
  };

  if (loading) {
    return (
      <>
        <PageTitle title="Agente IA" subtitle="Administracion" />
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
      <PageTitle title="Agente IA" subtitle="Administracion" />

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
                <h4 className="header-title mb-0">Agentes IA</h4>
                <button type="button" className="btn btn-sm btn-primary" onClick={addAgent} disabled={creating}>
                  <i className="ri-add-line me-1"></i>{creating ? 'Creando...' : 'Nuevo'}
                </button>
              </div>

              {agents.length === 0 ? (
                <div className="text-muted small">
                  No hay agentes creados todavia.
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
                          <div className="fw-semibold">{agent.name || 'Agente sin nombre'}</div>
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
                <h5 className="mb-2">Sin agentes IA</h5>
                <p className="text-muted mb-3">Crea tu primer agente para comenzar la configuracion.</p>
                <button type="button" className="btn btn-primary" onClick={addAgent} disabled={creating}>
                  <i className="ri-add-line me-1"></i>Crear agente
                </button>
              </div>
            </div>
          ) : (
            <div className="card">
              <div className="card-body">
                <ul className="nav nav-tabs nav-bordered mb-3">
                  {(['General', 'Mensajes', 'Captura de Datos', 'Configuracion IA'] as AgentTab[]).map((tab) => (
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
                    <h5 className="mb-3">Datos del agente</h5>
                    <div className="row">
                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">Nombre del Agente</label>
                          <input
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
                          <label className="form-label">Rol del agente</label>
                          <input
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

                    <h5 className="mb-3 mt-2">Estilo de comunicacion</h5>
                    <div className="row">
                      <div className="col-md-4">
                        <div className="mb-3">
                          <label className="form-label">Personalidad</label>
                          <input
                            type="text"
                            className="form-control"
                            value={selectedAgent.personality}
                            onChange={(e) =>
                              updateSelectedAgent((agent) => ({
                                ...agent,
                                personality: e.target.value,
                              }))
                            }
                          />
                        </div>
                      </div>
                      <div className="col-md-4">
                        <div className="mb-3">
                          <label className="form-label">Idioma</label>
                          <select
                            className="form-select"
                            value={selectedAgent.language}
                            onChange={(e) =>
                              updateSelectedAgent((agent) => ({
                                ...agent,
                                language: e.target.value,
                              }))
                            }
                          >
                            <option value="Espanol">Espanol</option>
                            <option value="Ingles">Ingles</option>
                            <option value="Portugues">Portugues</option>
                          </select>
                        </div>
                      </div>
                      <div className="col-md-4">
                        <div className="mb-3">
                          <label className="form-label">Tono</label>
                          <select
                            className="form-select"
                            value={selectedAgent.tone}
                            onChange={(e) =>
                              updateSelectedAgent((agent) => ({
                                ...agent,
                                tone: e.target.value,
                              }))
                            }
                          >
                            <option value="Amigable">Amigable</option>
                            <option value="Formal">Formal</option>
                            <option value="Directo">Directo</option>
                            <option value="Consultivo">Consultivo</option>
                          </select>
                        </div>
                      </div>
                    </div>

                    <h5 className="mb-3 mt-2">Informacion del Negocio</h5>
                    <div className="mb-3">
                      <label className="form-label">Nombre de la empresa</label>
                      <input
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
                      <label className="form-label">Descripcion del negocio</label>
                      <textarea
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
                      <label className="form-label">Productos y servicios</label>
                      <textarea
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
                    <h5 className="mb-3">Mensajes del agente</h5>
                    <div className="mb-3">
                      <label className="form-label">Mensaje de bienvenida</label>
                      <textarea
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
                      <label className="form-label">Mensaje de respaldo</label>
                      <textarea
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
                      <label className="form-label">Mensaje de cierre</label>
                      <textarea
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

                {activeTab === 'Captura de Datos' && (
                  <div>
                    <h5 className="mb-3">Campos de captura</h5>
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
                      <label className="form-check-label" htmlFor="ask-name">Solicitar nombre</label>
                    </div>
                    <div className="form-check mb-2">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="ask-phone"
                        checked={selectedAgent.askForPhone}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            askForPhone: e.target.checked,
                          }))
                        }
                      />
                      <label className="form-check-label" htmlFor="ask-phone">Solicitar telefono</label>
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
                      <label className="form-check-label" htmlFor="ask-email">Solicitar email</label>
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
                      <label className="form-check-label" htmlFor="ask-address">Solicitar direccion</label>
                    </div>

                    <div className="mb-0">
                      <label className="form-label">Preguntas personalizadas</label>
                      <textarea
                        className="form-control"
                        rows={4}
                        placeholder="Una pregunta por linea"
                        value={selectedAgent.customQuestions || ''}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            customQuestions: e.target.value,
                          }))
                        }
                      />
                    </div>
                  </div>
                )}

                {activeTab === 'Configuracion IA' && (
                  <div>
                    <h5 className="mb-3">Motor de IA</h5>
                    <div className="row">
                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">Modelo</label>
                          <select
                            className="form-select"
                            value={selectedAgent.model}
                            onChange={(e) =>
                              updateSelectedAgent((agent) => ({
                                ...agent,
                                model: e.target.value,
                              }))
                            }
                          >
                            <option value="gpt-4.1-mini">gpt-4.1-mini</option>
                            <option value="gpt-4.1">gpt-4.1</option>
                            <option value="gpt-4o-mini">gpt-4o-mini</option>
                          </select>
                        </div>
                      </div>
                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">Maximo de tokens</label>
                          <input
                            type="number"
                            className="form-control"
                            min={100}
                            max={4000}
                            value={selectedAgent.maxTokens}
                            onChange={(e) =>
                              updateSelectedAgent((agent) => ({
                                ...agent,
                                maxTokens: Number(e.target.value || 100),
                              }))
                            }
                          />
                        </div>
                      </div>
                    </div>

                    <div className="mb-3">
                      <label className="form-label">Temperatura: {selectedAgent.temperature.toFixed(1)}</label>
                      <input
                        type="range"
                        className="form-range"
                        min={0}
                        max={1}
                        step={0.1}
                        value={selectedAgent.temperature}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            temperature: Number(e.target.value),
                          }))
                        }
                      />
                    </div>

                    <div className="form-check mb-2">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="conversation-memory"
                        checked={selectedAgent.useConversationMemory}
                        onChange={(e) =>
                          updateSelectedAgent((agent) => ({
                            ...agent,
                            useConversationMemory: e.target.checked,
                          }))
                        }
                      />
                      <label className="form-check-label" htmlFor="conversation-memory">
                        Usar memoria conversacional
                      </label>
                    </div>
                    <div className="form-check mb-0">
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
                      <label className="form-check-label" htmlFor="strict-mode">Modo estricto</label>
                    </div>
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
                          ? 'Desactivar agente'
                          : 'Activar agente'}
                    </button>
                    <button
                      type="button"
                      className="btn btn-outline-danger"
                      onClick={deleteAgent}
                      disabled={deleting || saving || togglingActive}
                    >
                      {deleting ? 'Eliminando...' : 'Eliminar agente'}
                    </button>
                  </div>
                  <button type="button" className="btn btn-primary" onClick={saveAgent} disabled={saving || deleting}>
                    <i className="ri-save-line me-1"></i>{saving ? 'Guardando...' : 'Guardar configuracion'}
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
