import { apiRequest, type Schemas } from './typedClient';

/** What the permission page shows of an assistant's request (Fase 32E, ADR 0097). */
export type AssistantAuthorizationCheck = Schemas['AssistantAuthorizationCheckDto'];
/** An assistant connected to the customer account, as Mi cuenta lists it. */
export type MyAssistantConnection = Schemas['MyAssistantConnectionDto'];

/** The parameters of the request the assistant sent to /oauth/authorize, as the permission page received them. */
export interface AssistantAuthorizationRequest {
  responseType?: string;
  clientId?: string;
  redirectUri?: string;
  codeChallenge?: string;
  codeChallengeMethod?: string;
  resource?: string;
  scope?: string;
  state?: string;
}

/** The request of the page's address (/autorizar?client_id=…), with the names OAuth gives its parameters. */
export function authorizationRequestOf(search: URLSearchParams): AssistantAuthorizationRequest {
  const value = (name: string) => search.get(name) ?? undefined;
  return {
    responseType: value('response_type'),
    clientId: value('client_id'),
    redirectUri: value('redirect_uri'),
    codeChallenge: value('code_challenge'),
    codeChallengeMethod: value('code_challenge_method'),
    resource: value('resource'),
    scope: value('scope'),
    state: value('state'),
  };
}

/** The customer's account for AI assistants: the permission page and the assistants connected. */
export const assistantsApi = {
  /** Whether the request may ask the customer, with the assistant's name and where it returns; nothing is given. */
  check: (request: AssistantAuthorizationRequest, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/auth/assistant-authorizations', { query: { ...request }, signal }),

  /** The customer's answer, with their session: where the browser goes (the assistant, with its code or with access_denied). */
  answer: (request: AssistantAuthorizationRequest, allow: boolean) =>
    apiRequest('post', '/api/v1/auth/assistant-authorizations', { body: { ...request, allow } }),

  getMine: (signal?: AbortSignal) => apiRequest('get', '/api/v1/me/assistants', { signal }),

  /** Its tokens stop opening the account at its next request; one already gone is not found (404). */
  disconnect: (id: string) => apiRequest('delete', '/api/v1/me/assistants/{id}', { path: { id } }),
};
