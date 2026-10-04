import { useState, useRef } from 'react';
import { useForm } from 'react-hook-form';
import PageTitle from '../../components/common/PageTitle';
import UploadLoadingModal from '../../components/common/UploadLoadingModal';
import { useAuth } from '../../hooks/useAuth';
import { getUserRoleLabel } from '../../utils/labels';
import { profileApi } from '../../api/profileApi';
import type { UpdateProfileRequest, ChangePasswordRequest } from '../../types';
import { getApiErrorMessage } from '../../api/errors';

interface DisableTwoFactorForm {
  code: string;
}

export default function Profile() {
  const { user, refreshProfile } = useAuth();
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Edit profile state
  const [editMode, setEditMode] = useState(false);
  const [profileMsg, setProfileMsg] = useState<{ type: string; text: string } | null>(null);

  // Avatar state
  const [avatarLoading, setAvatarLoading] = useState(false);
  const [avatarUploading, setAvatarUploading] = useState(false);
  const [avatarMsg, setAvatarMsg] = useState<{ type: string; text: string } | null>(null);

  // Password state
  const [passwordMsg, setPasswordMsg] = useState<{ type: string; text: string } | null>(null);

  // Two-factor state
  const [twoFactorMsg, setTwoFactorMsg] = useState<{ type: string; text: string } | null>(null);
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);

  // Profile form
  const {
    register: regProfile,
    handleSubmit: handleProfile,
    formState: { isSubmitting: profileSubmitting },
  } = useForm<UpdateProfileRequest>({
    values: {
      firstName: user?.firstName || '',
      lastName: user?.lastName || '',
      phoneNumber: user?.phoneNumber || '',
    },
  });

  // Password form
  const {
    register: regPassword,
    handleSubmit: handlePassword,
    reset: resetPassword,
    formState: { errors: passwordErrors, isSubmitting: passwordSubmitting },
  } = useForm<ChangePasswordRequest>();

  // Disable 2FA form
  const {
    register: regTwoFactor,
    handleSubmit: handleTwoFactor,
    reset: resetTwoFactor,
    formState: { errors: twoFactorErrors, isSubmitting: twoFactorSubmitting },
  } = useForm<DisableTwoFactorForm>();

  const onUpdateProfile = async (data: UpdateProfileRequest) => {
    setProfileMsg(null);
    try {
      await profileApi.updateProfile(data);
      await refreshProfile();
      setEditMode(false);
      setProfileMsg({ type: 'success', text: 'Perfil actualizado correctamente.' });
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al actualizar el perfil.');
      setProfileMsg({ type: 'danger', text: msg });
    }
  };

  const onUploadAvatar = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setAvatarLoading(true);
    setAvatarUploading(true);
    setAvatarMsg(null);
    try {
      await profileApi.uploadAvatar(file);
      await refreshProfile();
      setAvatarMsg({ type: 'success', text: 'Avatar actualizado.' });
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al subir el avatar.');
      setAvatarMsg({ type: 'danger', text: msg });
    } finally {
      setAvatarLoading(false);
      setAvatarUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  const onDeleteAvatar = async () => {
    setAvatarLoading(true);
    setAvatarMsg(null);
    try {
      await profileApi.deleteAvatar();
      await refreshProfile();
      setAvatarMsg({ type: 'success', text: 'Avatar eliminado.' });
    } catch {
      setAvatarMsg({ type: 'danger', text: 'Error al eliminar el avatar.' });
    } finally {
      setAvatarLoading(false);
    }
  };

  const onChangePassword = async (data: ChangePasswordRequest) => {
    setPasswordMsg(null);
    try {
      await profileApi.changePassword(data);
      resetPassword();
      setPasswordMsg({ type: 'success', text: 'Contraseña actualizada. Tu sesión se cerrará pronto.' });
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al cambiar la contraseña.');
      setPasswordMsg({ type: 'danger', text: msg });
    }
  };

  const toggleTwoFactorCodeType = () => {
    setUseRecoveryCode((current) => !current);
    setTwoFactorMsg(null);
    resetTwoFactor({ code: '' });
  };

  const onDisableTwoFactor = async (data: DisableTwoFactorForm) => {
    setTwoFactorMsg(null);
    try {
      await profileApi.disableTwoFactor({ code: data.code, useRecoveryCode });
      resetTwoFactor({ code: '' });
      setUseRecoveryCode(false);
      // Set before refreshing: once `user` reports 2FA off, only this message keeps the card visible.
      setTwoFactorMsg({
        type: 'success',
        text: 'Verificación en dos pasos desactivada. Tu cuenta ya solo pide la contraseña para iniciar sesión.',
      });
      await refreshProfile();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'No se pudo desactivar la verificación en dos pasos.');
      setTwoFactorMsg({ type: 'danger', text: msg });
    }
  };

  const avatarSrc = user?.avatarImages?.medium || '/images/placeholders/avatar.svg';

  return (
    <>
      <UploadLoadingModal
        show={avatarLoading || avatarUploading}
        title="Subiendo avatar"
        description="Estamos actualizando tu foto de perfil."
      />

      <PageTitle title="Mi Perfil" subtitle="Cuenta" />

      {/* Profile Header */}
      <div className="row">
        <div className="col-sm-12">
          <div className="card bg-primary">
            <div className="card-body profile-user-box">
              <div className="row">
                <div className="col-sm-8">
                  <div className="row align-items-center">
                    <div className="col-auto">
                      <div className="avatar-lg">
                        <img
                          src={avatarSrc}
                          alt="avatar"
                          className="rounded-circle img-thumbnail"
                        />
                      </div>
                    </div>
                    <div className="col">
                      <div>
                        <h4 className="mt-1 mb-1 text-white">
                          {user?.firstName} {user?.lastName}
                        </h4>
                        <p className="font-13 text-white-50">{getUserRoleLabel(user?.role, user?.tenantRole)}</p>
                        <ul className="mb-0 list-inline text-light">
                          <li className="list-inline-item me-3">
                            <h5 className="mb-1 text-white">{user?.email}</h5>
                            <p className="mb-0 font-13 text-white-50">Email</p>
                          </li>
                          {user?.phoneNumber && (
                            <li className="list-inline-item">
                              <h5 className="mb-1 text-white">{user.phoneNumber}</h5>
                              <p className="mb-0 font-13 text-white-50">
                                Teléfono{' '}
                                {user.phoneNumberConfirmed ? (
                                  <span className="badge bg-success">Verificado</span>
                                ) : (
                                  <span className="badge bg-warning">Pendiente</span>
                                )}
                              </p>
                            </li>
                          )}
                        </ul>
                      </div>
                    </div>
                  </div>
                </div>
                <div className="col-sm-4">
                  <div className="text-center mt-sm-0 mt-3 text-sm-end">
                    <button
                      type="button"
                      className="btn btn-light"
                      onClick={() => setEditMode(!editMode)}
                    >
                      <i className="mdi mdi-account-edit me-1"></i>
                      {editMode ? 'Cancelar' : 'Editar Perfil'}
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="row">
        {/* Left Column */}
        <div className="col-xl-4">
          {/* Personal Information */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">Información Personal</h4>

              {profileMsg && (
                <div className={`alert alert-${profileMsg.type}`}>{profileMsg.text}</div>
              )}

              {editMode ? (
                <form onSubmit={handleProfile(onUpdateProfile)}>
                  <div className="mb-3">
                    <label className="form-label">Nombre</label>
                    <input
                      className="form-control"
                      type="text"
                      {...regProfile('firstName', { required: true })}
                    />
                  </div>
                  <div className="mb-3">
                    <label className="form-label">Apellido</label>
                    <input
                      className="form-control"
                      type="text"
                      {...regProfile('lastName', { required: true })}
                    />
                  </div>
                  <div className="mb-3">
                    <label className="form-label">Teléfono</label>
                    <input
                      className="form-control"
                      type="tel"
                      placeholder="+521234567890"
                      {...regProfile('phoneNumber')}
                    />
                    <small className="text-muted">
                      Si cambias el teléfono, deberás verificarlo de nuevo.
                    </small>
                  </div>
                  <button
                    className="btn btn-primary"
                    type="submit"
                    disabled={profileSubmitting}
                  >
                    {profileSubmitting ? 'Guardando...' : 'Guardar Cambios'}
                  </button>
                </form>
              ) : (
                <div className="text-start">
                  <p className="text-muted">
                    <strong>Nombre Completo:</strong>{' '}
                    <span className="ms-2">{user?.fullName}</span>
                  </p>
                  <p className="text-muted">
                    <strong>Email:</strong>{' '}
                    <span className="ms-2">{user?.email}</span>
                    {user?.emailConfirmed ? (
                      <span className="badge bg-success ms-1">Confirmado</span>
                    ) : (
                      <span className="badge bg-warning ms-1">Pendiente</span>
                    )}
                  </p>
                  <p className="text-muted">
                    <strong>Teléfono:</strong>{' '}
                    <span className="ms-2">{user?.phoneNumber || 'No registrado'}</span>
                  </p>
                  <p className="text-muted">
                    <strong>Rol:</strong>{' '}
                    <span className="ms-2">{getUserRoleLabel(user?.role, user?.tenantRole)}</span>
                  </p>
                  <p className="text-muted">
                    <strong>2FA:</strong>{' '}
                    <span className="ms-2">
                      {user?.twoFactorEnabled ? 'Activado' : 'Desactivado'}
                    </span>
                  </p>
                  <p className="text-muted mb-0">
                    <strong>Miembro desde:</strong>{' '}
                    <span className="ms-2">
                      {user?.createdAt
                        ? new Date(user.createdAt).toLocaleDateString('es-MX', {
                            year: 'numeric',
                            month: 'long',
                            day: 'numeric',
                          })
                        : ''}
                    </span>
                  </p>
                </div>
              )}
            </div>
          </div>

          {/* Avatar Section */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">Avatar</h4>

              {avatarMsg && (
                <div className={`alert alert-${avatarMsg.type}`}>{avatarMsg.text}</div>
              )}

              <div className="text-center mb-3">
                <img
                  src={avatarSrc}
                  alt="avatar"
                  className="rounded-circle img-thumbnail"
                  width="120"
                  height="120"
                />
              </div>

              <div className="d-flex gap-2 justify-content-center">
                <input
                  type="file"
                  ref={fileInputRef}
                  className="d-none"
                  accept="image/*"
                  onChange={onUploadAvatar}
                />
                <button
                  className="btn btn-primary btn-sm"
                  onClick={() => fileInputRef.current?.click()}
                  disabled={avatarLoading}
                >
                  <i className="mdi mdi-upload me-1"></i>
                  Subir Avatar
                </button>
                {user?.avatarUrl && (
                  <button
                    className="btn btn-danger btn-sm"
                    onClick={onDeleteAvatar}
                    disabled={avatarLoading}
                  >
                    <i className="mdi mdi-delete me-1"></i>
                    Eliminar
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>

        {/* Right Column */}
        <div className="col-xl-8">
          {/* Change Password */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">Cambiar Contraseña</h4>

              {passwordMsg && (
                <div className={`alert alert-${passwordMsg.type}`}>{passwordMsg.text}</div>
              )}

              <form onSubmit={handlePassword(onChangePassword)}>
                <div className="row">
                  <div className="col-md-4">
                    <div className="mb-3">
                      <label className="form-label">Contraseña Actual</label>
                      <input
                        type="password"
                        className={`form-control ${passwordErrors.currentPassword ? 'is-invalid' : ''}`}
                        {...regPassword('currentPassword', { required: 'Requerido' })}
                      />
                      {passwordErrors.currentPassword && (
                        <div className="invalid-feedback">
                          {passwordErrors.currentPassword.message}
                        </div>
                      )}
                    </div>
                  </div>
                  <div className="col-md-4">
                    <div className="mb-3">
                      <label className="form-label">Nueva Contraseña</label>
                      <input
                        type="password"
                        className={`form-control ${passwordErrors.newPassword ? 'is-invalid' : ''}`}
                        {...regPassword('newPassword', {
                          required: 'Requerido',
                          minLength: { value: 6, message: 'Mínimo 6 caracteres' },
                        })}
                      />
                      {passwordErrors.newPassword && (
                        <div className="invalid-feedback">
                          {passwordErrors.newPassword.message}
                        </div>
                      )}
                    </div>
                  </div>
                  <div className="col-md-4 d-flex align-items-end">
                    <div className="mb-3">
                      <button
                        className="btn btn-primary"
                        type="submit"
                        disabled={passwordSubmitting}
                      >
                        {passwordSubmitting ? 'Cambiando...' : 'Cambiar Contraseña'}
                      </button>
                    </div>
                  </div>
                </div>
              </form>
            </div>
          </div>

          {/* Two-factor authentication */}
          {(user?.twoFactorEnabled || twoFactorMsg) && (
            <div className="card">
              <div className="card-body">
                <h4 className="header-title mt-0 mb-3">Verificación en dos pasos</h4>

                {twoFactorMsg && (
                  <div className={`alert alert-${twoFactorMsg.type}`} role="alert">
                    {twoFactorMsg.text}
                  </div>
                )}

                {user?.twoFactorEnabled && (
                  <form onSubmit={handleTwoFactor(onDisableTwoFactor)}>
                    <p className="text-muted">
                      Tu cuenta pide un código de tu app de autenticación al iniciar sesión. Para desactivar la
                      verificación, ingresa ese código o, si perdiste el dispositivo, uno de tus códigos de
                      recuperación.
                    </p>
                    <div className="row">
                      <div className="col-md-6">
                        <div className="mb-3">
                          <label htmlFor="disableTwoFactorCode" className="form-label">
                            {useRecoveryCode ? 'Código de recuperación' : 'Código de verificación'}
                          </label>
                          <input
                            key={useRecoveryCode ? 'recovery' : 'authenticator'}
                            id="disableTwoFactorCode"
                            type="text"
                            className={`form-control ${twoFactorErrors.code ? 'is-invalid' : ''}`}
                            autoComplete={useRecoveryCode ? 'off' : 'one-time-code'}
                            inputMode={useRecoveryCode ? 'text' : 'numeric'}
                            maxLength={useRecoveryCode ? 32 : 7}
                            placeholder={useRecoveryCode ? 'XXXXX-XXXXX' : '000000'}
                            aria-invalid={twoFactorErrors.code ? 'true' : 'false'}
                            aria-describedby={twoFactorErrors.code ? 'disableTwoFactorCodeError' : undefined}
                            {...regTwoFactor('code', {
                              required: useRecoveryCode
                                ? 'El código de recuperación es requerido'
                                : 'El código es requerido',
                              validate: (value) =>
                                useRecoveryCode ||
                                /^\d{6}$/.test(value.replace(/[\s-]/g, '')) ||
                                'El código debe tener 6 dígitos',
                            })}
                          />
                          {twoFactorErrors.code && (
                            <div id="disableTwoFactorCodeError" className="invalid-feedback">
                              {twoFactorErrors.code.message}
                            </div>
                          )}
                        </div>
                      </div>
                      <div className="col-md-6 d-flex align-items-end">
                        <div className="mb-3">
                          <button className="btn btn-danger" type="submit" disabled={twoFactorSubmitting}>
                            {twoFactorSubmitting ? 'Desactivando...' : 'Desactivar 2FA'}
                          </button>
                        </div>
                      </div>
                    </div>
                    <button
                      type="button"
                      className="btn btn-link btn-sm text-muted p-0"
                      onClick={toggleTwoFactorCodeType}
                      disabled={twoFactorSubmitting}
                    >
                      {useRecoveryCode
                        ? 'Usar el código de la app de autenticación'
                        : 'Usar un código de recuperación'}
                    </button>
                  </form>
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    </>
  );
}
