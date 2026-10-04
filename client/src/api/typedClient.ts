import api from './axios';
import type { components, paths } from './generated/schema';

/**
 * Typed calls generated from the API's OpenAPI document (openapi/openapi.json → generated/schema.ts, with
 * `npm run api:pull` and `npm run api:types`). Paths, parameters, bodies and responses are checked by
 * TypeScript; the request still goes through the shared axios instance (token, refresh, business header).
 */
export type Schemas = components['schemas'];

type HttpMethod = 'get' | 'post' | 'put' | 'patch' | 'delete';

type PathsWith<M extends HttpMethod> = {
  [P in keyof paths]: paths[P][M & keyof paths[P]] extends { responses: unknown } ? P : never;
}[keyof paths];

type Operation<P extends keyof paths, M extends HttpMethod> = paths[P][M & keyof paths[P]];

type JsonContent<R> = R extends { content: { 'application/json': infer T } } ? T : void;

type SuccessBody<Op> = Op extends { responses: infer R }
  ? 200 extends keyof R
    ? JsonContent<R[200]>
    : 201 extends keyof R
      ? JsonContent<R[201]>
      : void
  : never;

type QueryOf<Op> = Op extends { parameters: { query?: infer Q } } ? Q : never;
type PathParamsOf<Op> = Op extends { parameters: { path?: infer P } } ? P : never;
type BodyOf<Op> = Op extends { requestBody?: { content: { 'application/json': infer B } } } ? B : never;

export interface TypedRequestOptions<Op> {
  path?: PathParamsOf<Op>;
  query?: QueryOf<Op>;
  body?: BodyOf<Op>;
  signal?: AbortSignal;
}

function toUrl(template: string, pathParams?: object): string {
  // The axios instance already points at /api/v1.
  let url = template.replace(/^\/api\/v1/, '');
  for (const [name, value] of Object.entries(pathParams ?? {})) {
    url = url.replace(`{${name}}`, encodeURIComponent(String(value)));
  }
  return url;
}

export async function apiRequest<M extends HttpMethod, P extends PathsWith<M>>(
  method: M,
  path: P,
  options: TypedRequestOptions<Operation<P, M>> = {},
): Promise<SuccessBody<Operation<P, M>>> {
  const { data } = await api.request<SuccessBody<Operation<P, M>>>({
    method,
    url: toUrl(path as string, options.path as object | undefined),
    params: options.query,
    data: options.body,
    signal: options.signal,
  });
  return data;
}
