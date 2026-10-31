import type { BusinessCategory } from './generated/enums';
import { apiRequest, type Schemas } from './typedClient';

export type PublicDirectory = Schemas['PublicDirectoryDto'];
export type DirectoryBusiness = Schemas['PublicDirectoryBusinessDto'];
/** A product a search of Explorar found (Fase 24F), with the business it is at. */
export type DirectoryProduct = Schemas['PublicDirectoryProductDto'];
export type DirectoryProfile = Schemas['DirectoryProfileDto'];
export type UpdateDirectoryProfile = Schemas['UpdateDirectoryProfileRequest'];

export interface DirectorySearch {
  category?: BusinessCategory;
  q?: string;
  /** The customer's postal code: each card says whether the business delivers there and what it costs (Fase 24B). */
  postalCode?: string;
  /** «Cerca de mí» (Fase 24G): where the customer is, to about 110 m; the nearest come first, each with its distance. */
  latitude?: number;
  longitude?: number;
  pageNumber?: number;
  pageSize?: number;
}

/** Explorar: the businesses that published their menu (no sign-in), and how the owner's business shows there. */
export const publicDirectoryApi = {
  getDirectory: (search: DirectorySearch, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/directory', { query: search, signal }),

  getProfile: () => apiRequest('get', '/api/v1/auth/my-tenant/directory'),

  updateProfile: (body: UpdateDirectoryProfile) => apiRequest('put', '/api/v1/auth/my-tenant/directory', { body }),

  /** «Ya lo revisé»: the notice of the published address of a business that goes to homes stops showing (Fase 29A). */
  dismissHomeVisitAddressNotice: () => apiRequest('post', '/api/v1/auth/my-tenant/directory/home-visit-address-notice/dismiss'),
};
