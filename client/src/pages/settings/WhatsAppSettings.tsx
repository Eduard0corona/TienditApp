import { useState, useEffect } from 'react';
import { useForm } from 'react-hook-form';
import PageTitle from '../../components/common/PageTitle';
import { tenantApi } from '../../api/tenantApi';
import type { WhatsAppConfigDto, UpdateWhatsAppConfigRequest, SendWhatsAppMessageRequest } from '../../types';
import { getApiErrorMessage } from '../../api/errors';

export default function WhatsAppSettings() {
  const [loading, setLoading] = useState(true);
  const [config, setConfig] = useState<WhatsAppConfigDto | null>(null);
  const [msg, setMsg] = useState<{ type: string; text: string } | null>(null);
  const [sendMsg, setSendMsg] = useState<{ type: string; text: string } | null>(null);

  const {
    register: registerConfig,
    handleSubmit: handleConfigSubmit,
    reset: resetConfig,
    watch,
    formState: { errors: configErrors, isSubmitting: configSubmitting },
  } = useForm<UpdateWhatsAppConfigRequest>();

  const {
    register: registerSend,
    handleSubmit: handleSendSubmit,
    reset: resetSend,
    formState: { errors: sendErrors, isSubmitting: sendSubmitting },
  } = useForm<SendWhatsAppMessageRequest>();

  const isEnabled = watch('isEnabled');

  useEffect(() => {
    const loadConfig = async () => {
      try {
        const { data } = await tenantApi.getWhatsAppConfig();
        setConfig(data);
        resetConfig({
          isEnabled: data.isEnabled,
          phoneNumberId: data.phoneNumberId || '',
          wabaId: data.wabaId || '',
          displayPhoneNumber: data.displayPhoneNumber || '',
          accessToken: '',
        });
      } catch {
        setMsg({ type: 'danger', text: 'Error al cargar la configuración de WhatsApp.' });
      } finally {
        setLoading(false);
      }
    };
    loadConfig();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onSaveConfig = async (data: UpdateWhatsAppConfigRequest) => {
    setMsg(null);
    try {
      // Don't send empty accessToken (keep existing)
      const payload = { ...data };
      if (!payload.accessToken) delete payload.accessToken;

      const { data: updated } = await tenantApi.updateWhatsAppConfig(payload);
      setConfig(updated);
      resetConfig({
        isEnabled: updated.isEnabled,
        phoneNumberId: updated.phoneNumberId || '',
        wabaId: updated.wabaId || '',
        displayPhoneNumber: updated.displayPhoneNumber || '',
        accessToken: '',
      });
      setMsg({ type: 'success', text: 'Configuración de WhatsApp guardada correctamente.' });
    } catch (err: unknown) {
      const errMsg = getApiErrorMessage(err, 'Error al guardar la configuración.');
      setMsg({ type: 'danger', text: errMsg });
    }
  };

  const onSendMessage = async (data: SendWhatsAppMessageRequest) => {
    setSendMsg(null);
    try {
      await tenantApi.sendWhatsAppMessage(data);
      setSendMsg({ type: 'success', text: 'Mensaje enviado exitosamente.' });
      resetSend();
    } catch (err: unknown) {
      const errMsg = getApiErrorMessage(err, 'Error al enviar el mensaje.');
      setSendMsg({ type: 'danger', text: errMsg });
    }
  };

  if (loading) {
    return (
      <>
        <PageTitle title="WhatsApp" />
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
      <PageTitle title="WhatsApp" />

      {/* Header */}
      <div className="row">
        <div className="col-sm-12">
          <div className="card bg-success">
            <div className="card-body profile-user-box">
              <div className="row align-items-center">
                <div className="col-auto">
                  <div className="avatar-lg">
                    <span className="avatar-title bg-white text-success rounded-circle fs-2">
                      <i className="ri-whatsapp-line"></i>
                    </span>
                  </div>
                </div>
                <div className="col">
                  <h4 className="mt-1 mb-1 text-white">WhatsApp Business</h4>
                  <p className="font-13 text-white mb-0">
                    Integración con Meta Cloud API para envío de mensajes
                  </p>
                </div>
                <div className="col-auto">
                  <span className={`badge ${config?.isEnabled ? 'bg-light text-success' : 'bg-light text-dark'} fs-6`}>
                    {config?.isEnabled ? 'Activo' : 'Inactivo'}
                  </span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      {msg && (
        <div className={`alert alert-${msg.type} alert-dismissible fade show`}>
          {msg.text}
          <button type="button" className="btn-close" onClick={() => setMsg(null)} />
        </div>
      )}

      <div className="row">
        {/* Configuration Card */}
        <div className="col-xl-6">
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">
                <i className="ri-settings-3-line me-1"></i>
                Configuración de API
              </h4>

              <form onSubmit={handleConfigSubmit(onSaveConfig)}>
                <div className="mb-3">
                  <div className="form-check form-switch">
                    <input
                      className="form-check-input"
                      type="checkbox"
                      id="isEnabled"
                      {...registerConfig('isEnabled')}
                    />
                    <label className="form-check-label" htmlFor="isEnabled">
                      Habilitar envío de WhatsApp
                    </label>
                  </div>
                </div>

                <div className="mb-3">
                  <label htmlFor="whatsappsettings-phone-number-id" className="form-label">
                    ID del número de teléfono {isEnabled && <span className="text-danger">*</span>}
                  </label>
                  <input id="whatsappsettings-phone-number-id"
                    className={`form-control ${configErrors.phoneNumberId ? 'is-invalid' : ''}`}
                    type="text"
                    placeholder="Ej: 123456789012345"
                    {...registerConfig('phoneNumberId', {
                      required: isEnabled ? 'Escribe el ID del número de teléfono.' : false,
                    })}
                  />
                  {configErrors.phoneNumberId && (
                    <div className="invalid-feedback">{configErrors.phoneNumberId.message}</div>
                  )}
                  <small className="text-muted">ID del número en Meta Business Suite</small>
                </div>

                <div className="mb-3">
                  <label htmlFor="whatsappsettings-waba-id" className="form-label">
                    ID de la cuenta de WhatsApp Business {isEnabled && <span className="text-danger">*</span>}
                  </label>
                  <input id="whatsappsettings-waba-id"
                    className={`form-control ${configErrors.wabaId ? 'is-invalid' : ''}`}
                    type="text"
                    placeholder="Ej: 123456789012345"
                    {...registerConfig('wabaId', {
                      required: isEnabled ? 'Escribe el ID de la cuenta de WhatsApp Business.' : false,
                    })}
                  />
                  {configErrors.wabaId && (
                    <div className="invalid-feedback">{configErrors.wabaId.message}</div>
                  )}
                  <small className="text-muted">En Meta Business Suite aparece como "WhatsApp Business Account ID" (WABA)</small>
                </div>

                <div className="mb-3">
                  <label htmlFor="whatsappsettings-access-token" className="form-label">Token de acceso</label>
                  <input id="whatsappsettings-access-token"
                    className="form-control"
                    type="password"
                    placeholder={config?.hasAccessToken ? '••••••••••••••••' : 'Pegar token de Meta'}
                    {...registerConfig('accessToken')}
                  />
                  <small className="text-muted">
                    {config?.hasAccessToken
                      ? 'Token configurado. Dejar vacío para mantener el actual.'
                      : 'Token permanente de Meta Business.'}
                  </small>
                </div>

                <div className="mb-3">
                  <label htmlFor="whatsappsettings-numero-visible" className="form-label">Número visible</label>
                  <input id="whatsappsettings-numero-visible"
                    className="form-control"
                    type="text"
                    placeholder="Ej: +5215512345678"
                    {...registerConfig('displayPhoneNumber')}
                  />
                  <small className="text-muted">Número de WhatsApp Business (informativo)</small>
                </div>

                <button className="btn btn-primary" type="submit" disabled={configSubmitting}>
                  {configSubmitting ? (
                    <>
                      <span className="spinner-border spinner-border-sm me-1" />
                      Guardando...
                    </>
                  ) : (
                    'Guardar configuración'
                  )}
                </button>
              </form>
            </div>
          </div>

          {/* Info Card */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">
                <i className="ri-information-line me-1"></i>
                Información
              </h4>

              <p className="text-muted mb-2">
                Los mensajes automáticos se envían cuando un cliente tiene número de teléfono registrado:
              </p>
              <ul className="text-muted mb-3">
                <li>Confirmación de pedido creado</li>
                <li>Actualización de estado del pedido</li>
                <li>Notificación de pedido cancelado</li>
              </ul>

              <h5 className="mb-2">Templates requeridos en Meta</h5>
              <p className="text-muted mb-2">
                Debes crear estos Message Templates en tu cuenta de Meta Business:
              </p>
              <div className="table-responsive">
                <table className="table table-sm table-bordered mb-0">
                  <thead className="table-light">
                    <tr>
                      <th>Template</th>
                      <th>Descripción</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td><code>order_confirmed</code></td>
                      <td>Pedido confirmado (params: #pedido, total)</td>
                    </tr>
                    <tr>
                      <td><code>order_status_update</code></td>
                      <td>Cambio de estado (params: #pedido, estado)</td>
                    </tr>
                    <tr>
                      <td><code>order_cancelled</code></td>
                      <td>Pedido cancelado (params: #pedido, razón)</td>
                    </tr>
                  </tbody>
                </table>
              </div>

              <div className="mt-3">
                <a
                  href="https://business.facebook.com/wa/manage/message-templates/"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="btn btn-outline-secondary btn-sm"
                >
                  <i className="ri-external-link-line me-1"></i>
                  Administrar plantillas en Meta
                </a>
              </div>
            </div>
          </div>
        </div>

        {/* Send Message Card */}
        <div className="col-xl-6">
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">
                <i className="ri-send-plane-line me-1"></i>
                Enviar mensaje
              </h4>

              {!config?.isEnabled && (
                <div className="alert alert-warning">
                  <i className="ri-alert-line me-1"></i>
                  Habilita WhatsApp en la configuración para enviar mensajes.
                </div>
              )}

              {sendMsg && (
                <div className={`alert alert-${sendMsg.type} alert-dismissible fade show`}>
                  {sendMsg.text}
                  <button type="button" className="btn-close" onClick={() => setSendMsg(null)} />
                </div>
              )}

              <form onSubmit={handleSendSubmit(onSendMessage)}>
                <div className="mb-3">
                  <label htmlFor="whatsappsettings-numero-de-telefono" className="form-label">
                    Número de teléfono <span className="text-danger">*</span>
                  </label>
                  <input id="whatsappsettings-numero-de-telefono"
                    className={`form-control ${sendErrors.phoneNumber ? 'is-invalid' : ''}`}
                    type="text"
                    placeholder="+5215512345678"
                    disabled={!config?.isEnabled}
                    {...registerSend('phoneNumber', {
                      required: 'El número es requerido',
                      pattern: {
                        value: /^\+[1-9]\d{6,14}$/,
                        message: 'Formato E.164 requerido (ej: +5215512345678)',
                      },
                    })}
                  />
                  {sendErrors.phoneNumber && (
                    <div className="invalid-feedback">{sendErrors.phoneNumber.message}</div>
                  )}
                </div>

                <div className="mb-3">
                  <label htmlFor="whatsappsettings-mensaje" className="form-label">
                    Mensaje <span className="text-danger">*</span>
                  </label>
                  <textarea id="whatsappsettings-mensaje"
                    className={`form-control ${sendErrors.message ? 'is-invalid' : ''}`}
                    rows={4}
                    placeholder="Escribe tu mensaje..."
                    disabled={!config?.isEnabled}
                    {...registerSend('message', {
                      required: 'El mensaje es requerido',
                      maxLength: { value: 4096, message: 'Máximo 4096 caracteres' },
                    })}
                  />
                  {sendErrors.message && (
                    <div className="invalid-feedback">{sendErrors.message.message}</div>
                  )}
                </div>

                <button
                  className="btn btn-success"
                  type="submit"
                  disabled={sendSubmitting || !config?.isEnabled}
                >
                  {sendSubmitting ? (
                    <>
                      <span className="spinner-border spinner-border-sm me-1" />
                      Enviando...
                    </>
                  ) : (
                    <>
                      <i className="ri-whatsapp-line me-1"></i>
                      Enviar por WhatsApp
                    </>
                  )}
                </button>
              </form>

              <div className="mt-3">
                <small className="text-muted">
                  <i className="ri-information-line me-1"></i>
                  Los mensajes de texto solo se pueden enviar dentro de la ventana de 24 horas
                  después de que el cliente haya enviado un mensaje. Para enviar fuera de esta ventana,
                  usa Message Templates aprobados por Meta.
                </small>
              </div>
            </div>
          </div>

          {/* Webhook Configuration Card */}
          {config?.verifyToken && (
            <div className="card border-success">
              <div className="card-body">
                <h4 className="header-title mt-0 mb-3">
                  <i className="ri-webhook-line me-1"></i>
                  Webhook para recibir mensajes
                </h4>
                <p className="text-muted mb-3">
                  Configura estos datos en <strong>Meta for Developers &gt; WhatsApp &gt; Configuration &gt; Webhook</strong> para recibir mensajes de tus clientes.
                </p>

                <div className="mb-3">
                  <label htmlFor="whatsappsettings-callback-url" className="form-label fw-semibold">URL de devolución de llamada (Callback URL)</label>
                  <div className="input-group">
                    <input id="whatsappsettings-callback-url"
                      type="text"
                      className="form-control font-monospace"
                      value={`${window.location.origin}/api/v1/whatsapp/webhook`}
                      readOnly
                    />
                    <button
                      className="btn btn-outline-secondary"
                      type="button"
                      aria-label="Copiar la URL de devolución de llamada"
                      onClick={() => navigator.clipboard.writeText(`${window.location.origin}/api/v1/whatsapp/webhook`)}
                    >
                      <i className="ri-clipboard-line" aria-hidden="true"></i>
                    </button>
                  </div>
                  <small className="text-muted">
                    URL pública donde Meta enviará los mensajes entrantes
                  </small>
                </div>

                <div className="mb-3">
                  <label htmlFor="whatsappsettings-verify-token" className="form-label fw-semibold">Token de verificación (Verify Token)</label>
                  <div className="input-group">
                    <input id="whatsappsettings-verify-token"
                      type="text"
                      className="form-control font-monospace"
                      value={config.verifyToken}
                      readOnly
                    />
                    <button
                      className="btn btn-outline-secondary"
                      type="button"
                      aria-label="Copiar el token de verificación"
                      onClick={() => navigator.clipboard.writeText(config.verifyToken!)}
                    >
                      <i className="ri-clipboard-line" aria-hidden="true"></i>
                    </button>
                  </div>
                  <small className="text-muted">Token de verificación para el webhook de Meta</small>
                </div>

                <div className="mb-0">
                  <div className="form-label fw-semibold">Campos del webhook</div>
                  <p className="text-muted mb-1">
                    Suscribe el webhook al campo <code>messages</code> en Meta for Developers.
                  </p>
                </div>

                <div className="alert alert-info mt-3 mb-0">
                  <i className="ri-information-line me-1"></i>
                  <strong>Importante:</strong> Tu servidor debe ser accesible desde internet (URL pública con HTTPS).
                  En desarrollo puedes usar <code>ngrok</code> para exponer tu servidor local.
                </div>
              </div>
            </div>
          )}

          {/* Getting Started Card */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">
                <i className="ri-question-line me-1"></i>
                ¿Cómo obtener las credenciales?
              </h4>
              <ol className="text-muted mb-0">
                <li className="mb-2">
                  Crea una cuenta en{' '}
                  <a href="https://business.facebook.com/" target="_blank" rel="noopener noreferrer">
                    Meta Business Suite
                  </a>
                </li>
                <li className="mb-2">
                  Ve a{' '}
                  <a href="https://developers.facebook.com/apps/" target="_blank" rel="noopener noreferrer">
                    Meta for Developers
                  </a>{' '}
                  y crea una app de tipo &quot;Business&quot;
                </li>
                <li className="mb-2">
                  Agrega el producto &quot;WhatsApp&quot; a tu app
                </li>
                <li className="mb-2">
                  En la sección de WhatsApp, encontrarás el <strong>Phone Number ID</strong> y el <strong>WABA ID</strong>
                </li>
                <li className="mb-2">
                  Genera un <strong>Access Token permanente</strong> en Configuración del Sistema &gt; Usuarios
                </li>
                <li className="mb-2">
                  Guarda la configuración aquí, luego copia la <strong>Callback URL</strong> y el <strong>Verify Token</strong> de la sección &quot;Webhook&quot; a Meta &gt; WhatsApp &gt; Configuration
                </li>
                <li className="mb-2">
                  Suscribe el webhook al campo <strong>messages</strong>
                </li>
                <li>
                  Crea los Message Templates necesarios y espera aprobación de Meta
                </li>
              </ol>
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
