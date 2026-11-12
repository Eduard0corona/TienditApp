import { useEffect, useState, useRef } from 'react';
import { useForm } from 'react-hook-form';
import PageTitle from '../../components/common/PageTitle';
import UploadLoadingModal from '../../components/common/UploadLoadingModal';
import { useAuth } from '../../hooks/useAuth';
import { getUserRoleLabel } from '../../utils/labels';
import { isOwnerRole } from '../../utils/teamRoles';
import { profileApi } from '../../api/profileApi';
import type { UpdateProfileRequest, ChangePasswordRequest } from '../../types';
import { getApiErrorMessage } from '../../api/errors';
import PhoneChangeCard from '../../components/profile/PhoneChangeCard';
import NoticeEmailsCard from '../../components/profile/NoticeEmailsCard';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import { formatInBusinessZone } from '../../utils/dates';
import { newPasswordPattern } from '../../utils/passwordRules';

interface DisableTwoFactorForm {
  code: string;
}

type Message = { type: string; text: string } | null;

/**
 * What a section has to say. A failure is announced as it happens (`role="alert"`); the rest in a `role="status"` that
 * is always on the page, empty while there is nothing to say, so screen readers announce what appears in it.
 */
function SectionNotice({ message }: { message: Message }) {
  return (
    <>
      <div role="status">
        {message && message.type !== 'danger' && <div className={`alert alert-${message.type}`}>{message.text}</div>}
      </div>
      {message?.type === 'danger' && <div className="alert alert-danger" role="alert">{message.text}</div>}
    </>
  );
}

const NAME_MAX_LENGTH = 100;

export default function Profile() {
  const { user, refreshProfile, logout } = useAuth();
  const { tenant, capabilities } = useTenant();
  // Fases 29N and 29O: whoever answers the requests online, or works the quotes, chooses which notices reach them by email.
  // The platform's admin acting in a business is not on its team: nothing to choose there.
  const access = getAccess(user, capabilities);
  const onTheTeam = !!tenant && !access.platform;
  const answersRequests = onTheTeam && access.appointments;
  const worksQuotes = onTheTeam && access.quotes;
  // Fase 30B: whoever confirms the orders, the online orders their customers cancel (and, since the Fase 31K, those waiting).
  const confirmsOrders = onTheTeam && access.orders;
  // Fase 31K: whoever reads and answers the reviews (owners and admins), the new ones.
  const answersReviews = onTheTeam && access.reviews;
  const fileInputRef = useRef<HTMLInputElement>(null);
  const editButtonRef = useRef<HTMLButtonElement>(null);
  // Saved, the form it was in goes away: the focus goes back to what opened it, once it is enabled again.
  const focusEditButton = useRef(false);
  const signOutTimeoutRef = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(signOutTimeoutRef.current), []);

  // Edit profile state
  const [editMode, setEditMode] = useState(false);
  const [profileMsg, setProfileMsg] = useState<Message>(null);

  // Avatar state
  const [avatarLoading, setAvatarLoading] = useState(false);
  const [avatarUploading, setAvatarUploading] = useState(false);
  const [avatarMsg, setAvatarMsg] = useState<Message>(null);

  // Password state
  const [passwordMsg, setPasswordMsg] = useState<Message>(null);

  // Two-factor state
  const [twoFactorMsg, setTwoFactorMsg] = useState<Message>(null);
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);

  // Profile form
  const {
    register: regProfile,
    handleSubmit: handleProfile,
    reset: resetProfile,
    formState: { errors: profileErrors, isSubmitting: profileSubmitting },
  } = useForm<UpdateProfileRequest>({
    values: {
      firstName: user?.firstName || '',
      lastName: user?.lastName || '',
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

  useEffect(() => {
    if (!focusEditButton.current || profileSubmitting) return;
    focusEditButton.current = false;
    editButtonRef.current?.focus();
  }, [editMode, profileSubmitting]);

  const onUpdateProfile = async (data: UpdateProfileRequest) => {
    setProfileMsg(null);
    try {
      await profileApi.updateProfile(data);
      await refreshProfile();
      setEditMode(false);
      setProfileMsg({ type: 'success', text: 'Perfil actualizado correctamente.' });
      focusEditButton.current = true;
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
    } catch (err: unknown) {
      setAvatarMsg({ type: 'danger', text: getApiErrorMessage(err, 'Error al eliminar el avatar.') });
    } finally {
      setAvatarLoading(false);
    }
  };

  const onChangePassword = async (data: ChangePasswordRequest) => {
    setPasswordMsg(null);
    try {
      await profileApi.changePassword(data);
      resetPassword();
      // The server ended every session of the account, this one too: the person signs in again with the new one.
      setPasswordMsg({
        type: 'success',
        text: 'Contraseña actualizada. Cerramos tus sesiones en todos los dispositivos; inicia sesión con la nueva contraseña.',
      });
      signOutTimeoutRef.current = window.setTimeout(() => void logout(), 3000);
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

  // Not while the name is being saved: its answer reaches the form that sent it. What the previous mode said goes away.
  const toggleEdit = () => {
    if (editMode) resetProfile();
    setProfileMsg(null);
    setEditMode(!editMode);
  };

  const nameRules = (missing: string) => ({
    validate: (value: string) => !!value.trim() || missing,
    maxLength: { value: NAME_MAX_LENGTH, message: `Escribe hasta ${NAME_MAX_LENGTH} caracteres.` },
  });

  return (
    <>
      {/* Only while a photo uploads: deleting one only disables its buttons. */}
      <UploadLoadingModal
        show={avatarUploading}
        title="Subiendo avatar"
        description="Estamos actualizando tu foto de perfil."
      />

      <PageTitle title="Mi perfil" />

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
                          alt="Tu foto de perfil"
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
                            <p className="mb-0 font-13 text-white-50">Correo</p>
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
                      ref={editButtonRef}
                      type="button"
                      className="btn btn-light"
                      // Cancelled, what was typed is forgotten: the next edit starts from the profile.
                      onClick={toggleEdit}
                      disabled={profileSubmitting}
                      aria-label={editMode ? 'Cancelar la edición del perfil' : undefined}
                    >
                      <i className="ri-user-settings-line me-1" aria-hidden="true"></i>
                      {editMode ? 'Cancelar' : 'Editar perfil'}
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
              <h4 className="header-title mt-0 mb-3">Información personal</h4>

              <SectionNotice message={profileMsg} />

              {editMode ? (
                <form onSubmit={handleProfile(onUpdateProfile)}>
                  <div className="mb-3">
                    <label htmlFor="profile-nombre" className="form-label">Nombre</label>
                    <input id="profile-nombre"
                      className={`form-control ${profileErrors.firstName ? 'is-invalid' : ''}`}
                      type="text"
                      aria-invalid={profileErrors.firstName ? 'true' : undefined}
                      aria-describedby={profileErrors.firstName ? 'profile-nombre-error' : undefined}
                      maxLength={NAME_MAX_LENGTH}
                      {...regProfile('firstName', nameRules('Escribe tu nombre.'))}
                    />
                    {profileErrors.firstName && (
                      <div id="profile-nombre-error" className="invalid-feedback">{profileErrors.firstName.message}</div>
                    )}
                  </div>
                  <div className="mb-3">
                    <label htmlFor="profile-apellido" className="form-label">Apellido</label>
                    <input id="profile-apellido"
                      className={`form-control ${profileErrors.lastName ? 'is-invalid' : ''}`}
                      type="text"
                      aria-invalid={profileErrors.lastName ? 'true' : undefined}
                      aria-describedby={profileErrors.lastName ? 'profile-apellido-error' : undefined}
                      maxLength={NAME_MAX_LENGTH}
                      {...regProfile('lastName', nameRules('Escribe tu apellido.'))}
                    />
                    {profileErrors.lastName && (
                      <div id="profile-apellido-error" className="invalid-feedback">{profileErrors.lastName.message}</div>
                    )}
                  </div>
                  <button
                    className="btn btn-primary"
                    type="submit"
                    disabled={profileSubmitting}
                  >
                    {profileSubmitting ? 'Guardando...' : 'Guardar cambios'}
                  </button>
                </form>
              ) : (
                <div className="text-start">
                  <p className="text-muted">
                    <strong>Nombre completo:</strong>{' '}
                    <span className="ms-2">{user?.fullName}</span>
                  </p>
                  <p className="text-muted">
                    <strong>Correo:</strong>{' '}
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
                        ? formatInBusinessZone(user.createdAt, {
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

          <PhoneChangeCard />

          {(answersRequests || worksQuotes || confirmsOrders || answersReviews) && (
            <NoticeEmailsCard
              businessName={tenant?.name}
              agenda={answersRequests}
              quotes={worksQuotes}
              orders={confirmsOrders}
              reviews={answersReviews}
              ownsBusiness={isOwnerRole(user?.tenantRole)}
            />
          )}

          {/* Avatar Section */}
          <div className="card">
            <div className="card-body">
              <h4 className="header-title mt-0 mb-3">Foto de perfil</h4>

              <SectionNotice message={avatarMsg} />

              <div className="text-center mb-3">
                <img
                  src={avatarSrc}
                  alt="Tu foto de perfil"
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
                  <i className="ri-upload-2-line me-1" aria-hidden="true"></i>
                  Subir foto
                </button>
                {user?.avatarUrl && (
                  <button
                    className="btn btn-danger btn-sm"
                    onClick={onDeleteAvatar}
                    disabled={avatarLoading}
                  >
                    <i className="ri-delete-bin-line me-1" aria-hidden="true"></i>
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
              <h4 className="header-title mt-0 mb-3">Cambiar contraseña</h4>

              <SectionNotice message={passwordMsg} />

              <form onSubmit={handlePassword(onChangePassword)}>
                <div className="row">
                  <div className="col-md-4">
                    <div className="mb-3">
                      <label htmlFor="profile-contrasena-actual" className="form-label">Contraseña actual</label>
                      <input id="profile-contrasena-actual"
                        type="password"
                        className={`form-control ${passwordErrors.currentPassword ? 'is-invalid' : ''}`}
                        aria-invalid={passwordErrors.currentPassword ? 'true' : undefined}
                        aria-describedby={passwordErrors.currentPassword ? 'profile-contrasena-actual-error' : undefined}
                        {...regPassword('currentPassword', { required: 'Requerido' })}
                      />
                      {passwordErrors.currentPassword && (
                        <div id="profile-contrasena-actual-error" className="invalid-feedback">
                          {passwordErrors.currentPassword.message}
                        </div>
                      )}
                    </div>
                  </div>
                  <div className="col-md-4">
                    <div className="mb-3">
                      <label htmlFor="profile-nueva-contrasena" className="form-label">Nueva contraseña</label>
                      <input id="profile-nueva-contrasena"
                        type="password"
                        className={`form-control ${passwordErrors.newPassword ? 'is-invalid' : ''}`}
                        aria-invalid={passwordErrors.newPassword ? 'true' : undefined}
                        aria-describedby={passwordErrors.newPassword ? 'profile-nueva-contrasena-error' : undefined}
                        {...regPassword('newPassword', {
                          required: 'Requerido',
                          pattern: newPasswordPattern,
                        })}
                      />
                      {passwordErrors.newPassword && (
                        <div id="profile-nueva-contrasena-error" className="invalid-feedback">
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
                        {passwordSubmitting ? 'Cambiando...' : 'Cambiar contraseña'}
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

                <SectionNotice message={twoFactorMsg} />

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
