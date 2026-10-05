import { isAxiosError } from 'axios';

// Every API error is ProblemDetails (application/problem+json) with a stable `code` (validation_failed,
// business_rule, module_disabled, concurrency_conflict...), `detail` in Spanish, `errors` per field on
// validation and `error` repeating `detail` for older readers. Decide by `code`, never by `title` or the
// wording. Screens read errors only through these helpers.

interface ApiErrorBody {
  code?: unknown;
  error?: unknown;
  detail?: unknown;
  title?: unknown;
  errors?: unknown;
}

/** The codes the API answers (see ApiErrorCodes in the WebApi project). */
export type ApiErrorCode =
  | 'validation_failed'
  | 'business_rule'
  | 'domain_rule'
  | 'unauthorized'
  | 'forbidden'
  | 'tenant_required'
  | 'tenant_access_denied'
  | 'tenant_suspended'
  | 'module_disabled'
  | 'not_found'
  | 'conflict'
  | 'concurrency_conflict'
  | 'invalid_state_transition'
  | 'too_many_requests'
  | 'server_error'
  | 'bad_request';

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

/** The `code` of an API error (or undefined for errors without an answer or from older servers). */
export function getApiErrorCode(err: unknown): ApiErrorCode | string | undefined {
  return nonEmptyString(getBody(err)?.code);
}

/**
 * One message to show for a failed request, chosen by the error `code`: the few situations every screen
 * words the same (a module turned off, someone else saved first, the status moved on) use one message;
 * validation shows the first field error; anything else shows the server's text. Server errors (5xx) and
 * unknown answers fall back to the message of the screen.
 */
export function getApiErrorMessage(err: unknown, fallback: string): string {
  if (!isAxiosError(err)) return fallback;
  if (!err.response) return err.code === 'ERR_CANCELED' ? fallback : NETWORK_MESSAGE;

  const { status } = err.response;
  if (status === 429) return TOO_MANY_REQUESTS_MESSAGE;
  if (status >= 500) return fallback;

  const body = getBody(err);
  const code = nonEmptyString(body?.code);
  const title = nonEmptyString(body?.title);
  const text = nonEmptyString(body?.detail) ?? nonEmptyString(body?.error);
  const firstFieldError = Object.values(getApiFieldErrors(err))[0]?.[0];

  // An answer without code comes from an older server: its titles meant the same.
  if (code === 'module_disabled' || title === 'Tenant capability denied') return MODULE_DISABLED_MESSAGE;
  if (code === 'concurrency_conflict' || (!code && title === 'Concurrency Conflict')) return CONCURRENCY_MESSAGE;
  if (code === 'invalid_state_transition' || (!code && title === 'Invalid State Transition')) return STATE_CHANGED_MESSAGE;
  if (code === 'validation_failed') return firstFieldError ?? text ?? fallback;
  if (!code) {
    const explicit = nonEmptyString(body?.error);
    if (explicit) return explicit;
    if (status === 403) return FORBIDDEN_MESSAGE;
    if (status === 404 && title === 'Entity Not Found') return NOT_FOUND_MESSAGE;
    return firstFieldError ?? nonEmptyString(body?.detail) ?? fallback;
  }

  if (text) return text;
  if (status === 403) return FORBIDDEN_MESSAGE;
  if (status === 404) return NOT_FOUND_MESSAGE;
  return firstFieldError ?? fallback;
}
