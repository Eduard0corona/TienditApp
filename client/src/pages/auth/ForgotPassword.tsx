import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link } from 'react-router-dom';
import { authApi } from '../../api/authApi';

interface ForgotPasswordForm {
  email: string;
}

export default function ForgotPassword() {
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ForgotPasswordForm>();

  const onSubmit = async (data: ForgotPasswordForm) => {
    setError('');
    try {
      await authApi.forgotPassword(data.email);
      setSuccess(true);
    } catch {
      setError('Error al enviar el correo de recuperación.');
    }
  };

  return (
    <div className="authentication-bg position-relative" style={{ minHeight: '100vh' }}>
      <div className="position-absolute start-0 end-0 start-0 bottom-0 w-100 h-100">
        <svg xmlns="http://www.w3.org/2000/svg" width="100%" height="100%" viewBox="0 0 800 800">
          <g fillOpacity="0.22">
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.1)' }} cx="400" cy="400" r="600" />
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.2)' }} cx="400" cy="400" r="500" />
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.3)' }} cx="400" cy="400" r="300" />
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.4)' }} cx="400" cy="400" r="200" />
            <circle style={{ fill: 'rgba(var(--ct-primary-rgb), 0.5)' }} cx="400" cy="400" r="100" />
          </g>
        </svg>
      </div>
      <div className="account-pages pt-2 pt-sm-5 pb-4 pb-sm-5 position-relative">
        <div className="container">
          <div className="row justify-content-center">
            <div className="col-xxl-4 col-lg-5">
              <div className="card">
                <div className="card-header py-4 text-center bg-primary">
                  <Link to="/">
                    <img src="/images/logo.png" alt="logo" height="22" />
                  </Link>
                </div>

                <div className="card-body p-4">
                  <div className="text-center w-75 m-auto">
                    <h4 className="text-dark-50 text-center pb-0 fw-bold">Recuperar Contraseña</h4>
                    <p className="text-muted mb-4">
                      Ingresa tu email y te enviaremos instrucciones para restablecer tu contraseña.
                    </p>
                  </div>

                  {error && <div className="alert alert-danger">{error}</div>}

                  {success ? (
                    <div className="alert alert-success text-center">
                      <i className="ri-check-line me-1"></i>
                      Si el email existe en nuestro sistema, recibirás un correo con instrucciones.
                    </div>
                  ) : (
                    <form onSubmit={handleSubmit(onSubmit)}>
                      <div className="mb-3">
                        <label htmlFor="emailaddress" className="form-label">Email</label>
                        <input
                          className={`form-control ${errors.email ? 'is-invalid' : ''}`}
                          type="email"
                          id="emailaddress"
                          placeholder="Ingresa tu email"
                          {...register('email', { required: 'El email es requerido' })}
                        />
                        {errors.email && <div className="invalid-feedback">{errors.email.message}</div>}
                      </div>

                      <div className="mb-0 text-center">
                        <button className="btn btn-primary" type="submit" disabled={isSubmitting}>
                          {isSubmitting ? 'Enviando...' : 'Enviar'}
                        </button>
                      </div>
                    </form>
                  )}
                </div>
              </div>

              <div className="row mt-3">
                <div className="col-12 text-center">
                  <p className="text-muted">
                    <Link to="/login" className="text-muted ms-1">
                      <b>Volver al login</b>
                    </Link>
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
