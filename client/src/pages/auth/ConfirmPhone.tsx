import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { authApi } from '../../api/authApi';

interface ConfirmPhoneForm {
  code: string;
}

export default function ConfirmPhone() {
  const navigate = useNavigate();
  const location = useLocation();
  const userId = (location.state as { userId?: string })?.userId || '';
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ConfirmPhoneForm>();

  const onSubmit = async (data: ConfirmPhoneForm) => {
    setError('');
    try {
      await authApi.confirmPhone(userId, data.code);
      setSuccess('Número confirmado. Ahora puedes iniciar sesión.');
      setTimeout(() => navigate('/login'), 2000);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        'Código inválido';
      setError(msg);
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
                    <h4 className="text-dark-50 text-center pb-0 fw-bold">Confirmar Teléfono</h4>
                    <p className="text-muted mb-4">
                      Ingresa el código de 6 dígitos que recibiste por SMS.
                    </p>
                  </div>

                  {error && <div className="alert alert-danger">{error}</div>}
                  {success && <div className="alert alert-success">{success}</div>}

                  <form onSubmit={handleSubmit(onSubmit)}>
                    <div className="mb-3">
                      <label htmlFor="code" className="form-label">Código SMS</label>
                      <input
                        className={`form-control text-center ${errors.code ? 'is-invalid' : ''}`}
                        type="text"
                        id="code"
                        maxLength={6}
                        placeholder="000000"
                        style={{ fontSize: '1.5rem', letterSpacing: '0.5rem' }}
                        {...register('code', {
                          required: 'El código es requerido',
                          minLength: { value: 6, message: 'El código debe tener 6 dígitos' },
                          maxLength: { value: 6, message: 'El código debe tener 6 dígitos' },
                        })}
                      />
                      {errors.code && <div className="invalid-feedback">{errors.code.message}</div>}
                    </div>

                    <div className="mb-3 text-center">
                      <button className="btn btn-primary" type="submit" disabled={isSubmitting}>
                        {isSubmitting ? 'Verificando...' : 'Verificar'}
                      </button>
                    </div>
                  </form>
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
