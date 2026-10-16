import type { BusinessCategory } from './generated/enums';
import { apiRequest, type Schemas } from './typedClient';

export type PublicDirectory = Schemas['PublicDirectoryDto'];
export type DirectoryBusiness = Schemas['PublicDirectoryBusinessDto'];
export type DirectoryProfile = Schemas['DirectoryProfileDto'];
export type UpdateDirectoryProfile = Schemas['UpdateDirectoryProfileRequest'];

export interface DirectorySearch {
  category?: BusinessCategory;
  q?: string;
  pageNumber?: number;
  pageSize?: number;
}

/** Explorar: the businesses that published their menu (no sign-in), and how the owner's business shows there. */
export const publicDirectoryApi = {
  getDirectory: (search: DirectorySearch, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/directory', { query: search, signal }),

  getProfile: () => apiRequest('get', '/api/v1/auth/my-tenant/directory'),

  updateProfile: (body: UpdateDirectoryProfile) => apiRequest('put', '/api/v1/auth/my-tenant/directory', { body }),
};
