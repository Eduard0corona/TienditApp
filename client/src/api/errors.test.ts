import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { describe, expect, it } from 'vitest';
import { getApiErrorCode, getApiErrorMessage, getApiFieldErrors } from './errors';

function apiError(status: number, data?: unknown, code?: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = data === undefined
    ? undefined
    : ({ status, data, statusText: '', headers, config: { headers } } as AxiosResponse);
  return new AxiosError('Request failed', code, { headers }, null, response);
}

describe('getApiErrorMessage', () => {
  it('shows the first field error of a validation problem', () => {
    const err = apiError(400, {
      code: 'validation_failed',
      detail: 'Revisa los datos.',
      errors: { 'Items[0].Quantity': ['La cantidad debe ser mayor a cero.'] },
    });

    expect(getApiErrorMessage(err, 'No se pudo guardar.')).toBe('La cantidad debe ser mayor a cero.');
    expect(getApiFieldErrors(err)).toEqual({ 'items[0].quantity': ['La cantidad debe ser mayor a cero.'] });
  });

  it('words a module that is off the same way on every screen', () => {
    const err = apiError(403, { code: 'module_disabled', detail: 'El módulo no está activo en este negocio.' });

    expect(getApiErrorCode(err)).toBe('module_disabled');
    expect(getApiErrorMessage(err, 'x')).toBe('Este módulo no está habilitado para tu negocio.');
  });

  it("shows the server's text for business rules and permissions", () => {
    const refused = apiError(403, { code: 'forbidden', detail: 'No tienes permiso para hacer reembolsos.' });

    expect(getApiErrorMessage(refused, 'x')).toBe('No tienes permiso para hacer reembolsos.');
  });

  it('falls back to the screen message on server errors and asks to retry without connection', () => {
    expect(getApiErrorMessage(apiError(500, { code: 'server_error', detail: 'Boom' }), 'No se pudo guardar.')).toBe('No se pudo guardar.');
    expect(getApiErrorMessage(apiError(0), 'x')).toBe('No hay conexión con el servidor. Revisa tu internet e intenta de nuevo.');
    expect(getApiErrorMessage(apiError(0, undefined, 'ERR_CANCELED'), 'Cancelado')).toBe('Cancelado');
    expect(getApiErrorMessage(new Error('not axios'), 'Algo falló')).toBe('Algo falló');
  });

  it('asks to wait after too many attempts', () => {
    expect(getApiErrorMessage(apiError(429, {}), 'x')).toBe('Demasiados intentos seguidos. Espera un minuto e intenta de nuevo.');
  });
});
