import { apiRequest, type Schemas } from './typedClient';

export type Branch = Schemas['BranchDto'];
export type CreateBranchInput = Schemas['CreateBranchCommand'];
export type UpdateBranchInput = Schemas['UpdateBranchCommand'];

/** Branches of the business: every member reads them; owners and admins manage them. */
export const branchesApi = {
  getAll: (isActive?: boolean) =>
    apiRequest('get', '/api/v1/branches', { query: { isActive } }),

  create: (body: CreateBranchInput) =>
    apiRequest('post', '/api/v1/branches', { body }),

  update: (id: string, body: UpdateBranchInput) =>
    apiRequest('put', '/api/v1/branches/{id}', { path: { id }, body }),

  setActive: (id: string, isActive: boolean) =>
    apiRequest('patch', '/api/v1/branches/{id}/active', { path: { id }, body: { isActive } }),

  setDefault: (id: string) =>
    apiRequest('post', '/api/v1/branches/{id}/default', { path: { id } }),
};
