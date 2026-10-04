import { isAxiosError } from 'axios';

// The API answers errors in a few shapes: `{ error }` from controllers, ProblemDetails (`title`,
// `detail`) from the exception handler and the tenant middleware, and validation ProblemDetails with
// `errors` per field. Screens read them only through these helpers.

interface ApiErrorBody {
  error?: unknown;
  detail?: unknown;
  title?: unknown;
  errors?: unknown;
}

const NETWORK_MESSAGE = 'No hay conexión con el servidor. Revisa tu internet e intenta de nuevo.';
const TOO_MANY_REQUESTS_MESSAGE = 'Demasiados intentos seguidos. Espera un minuto e intenta de nuevo.';
const FORBIDDEN_MESSAGE = 'No tienes permiso para realizar esta acción.';
const MODULE_DISABLED_MESSAGE = 'Este módulo no está habilitado para tu negocio.';
const NOT_FOUND_MESSAGE = 'No se encontró el registro. Es posible que ya no exista.';
const CONCURRENCY_MESSAGE = 'Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.';
const STATE_CHANGED_MESSAGE = 'El estado cambió mientras lo veías. Recarga para ver el estado actual.';

function nonEmptyString(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value.trim() : undefined;
}

function getBody(err: unknown): ApiErrorBody | undefined {
  if (!isAxiosError(err)) return undefined;
  const data: unknown = err.response?.data;
  return data && typeof data === 'object' ? (data as ApiErrorBody) : undefined;
}

function toCamelCase(field: string): string {
  // "Items[0].Quantity" -> "items[0].quantity"; "$.newStatus" (JSON binding) -> "newStatus".
  return field
    .replace(/^\$\.?/, '')
    .split('.')
    .map((part) => (part ? part[0].toLowerCase() + part.slice(1) : part))
    .join('.');
}

/** Field errors of a validation response, keyed by the camelCase name used by the forms. */
export function getApiFieldErrors(err: unknown): Record<string, string[]> {
  const errors = getBody(err)?.errors;
  if (!errors || typeof errors !== 'object' || Array.isArray(errors)) return {};

  const result: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(errors as Record<string, unknown>)) {
    const list = (Array.isArray(messages) ? messages : [messages])
      .map(nonEmptyString)
      .filter((message): message is string => message !== undefined);
    if (list.length > 0) result[toCamelCase(field)] = list;
  }
  return result;
}

/** HTTP status of an API error, or undefined when the request never got an answer. */
export function getApiErrorStatus(err: unknown): number | undefined {
  return isAxiosError(err) ? err.response?.status : undefined;
}

/**
 * One message to show for a failed request. Reads `error`, then the first field error, then `detail`;
 * server errors (5xx) and permission errors use a clear Spanish message instead of technical text,
 * and anything unknown falls back to the message of the screen.
 */
export function getApiErrorMessage(err: unknown, fallback: string): string {
  if (!isAxiosError(err)) return fallback;
  if (!err.response) return err.code === 'ERR_CANCELED' ? fallback : NETWORK_MESSAGE;

  const { status } = err.response;
  if (status === 429) return TOO_MANY_REQUESTS_MESSAGE;
  if (status >= 500) return fallback;

  const body = getBody(err);
  const explicit = nonEmptyString(body?.error);
  if (explicit) return explicit;

  const title = nonEmptyString(body?.title);
  if (status === 403) return title === 'Tenant capability denied' ? MODULE_DISABLED_MESSAGE : FORBIDDEN_MESSAGE;
  if (status === 409 && title === 'Concurrency Conflict') return CONCURRENCY_MESSAGE;
  if (status === 409 && title === 'Invalid State Transition') return STATE_CHANGED_MESSAGE;
  if (status === 404 && title === 'Entity Not Found') return NOT_FOUND_MESSAGE;

  const firstFieldError = Object.values(getApiFieldErrors(err))[0]?.[0];
  if (firstFieldError) return firstFieldError;

  return nonEmptyString(body?.detail) ?? fallback;
}
