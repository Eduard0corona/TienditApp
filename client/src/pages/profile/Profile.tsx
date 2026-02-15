import { useState, useRef } from 'react';
import { useForm } from 'react-hook-form';
import PageTitle from '../../components/common/PageTitle';
import { useAuth } from '../../hooks/useAuth';
import { profileApi } from '../../api/profileApi';
import type { UpdateProfileRequest, ChangePasswordRequest } from '../../types';

export default function Profile() {
  const { user, refreshProfile } = useAuth();
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Edit profile state
  const [editMode, setEditMode] = useState(false);
  const [profileMsg, setProfileMsg] = useState<{ type: string; text: string } | null>(null);

  // Avatar state
  const [avatarLoading, setAvatarLoading] = useState(false);
  const [avatarMsg, setAvatarMsg] = useState<{ type: string; text: string } | null>(null);

  // Password state
  const [passwordMsg, setPasswordMsg] = useState<{ type: string; text: string } | null>(null);

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

  const onUpdateProfile = async (data: UpdateProfileRequest) => {
    setProfileMsg(null);
    try {
      await profileApi.updateProfile(data);
      await refreshProfile();
      setEditMode(false);
      setProfileMsg({ type: 'success', text: 'Perfil actualizado correctamente.' });
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        'Error al actualizar el perfil.';
      setProfileMsg({ type: 'danger', text: msg });
    }
  };

  const onUploadAvatar = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setAvatarLoading(true);
    setAvatarMsg(null);
    try {
      await profileApi.uploadAvatar(file);
      await refreshProfile();
      setAvatarMsg({ type: 'success', text: 'Avatar actualizado.' });
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        'Error al subir el avatar.';
      setAvatarMsg({ type: 'danger', text: msg });
    } finally {
      setAvatarLoading(false);
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
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        'Error al cambiar la contraseña.';
      setPasswordMsg({ type: 'danger', text: msg });
    }
  };

  const avatarSrc = user?.avatarImages?.medium || '/images/users/avatar-1.jpg';

  return (
    <>
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
                        <p className="font-13 text-white-50">{user?.role}</p>
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
                    <span className="ms-2">{user?.role}</span>
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
        </div>
      </div>
    </>
  );
}
