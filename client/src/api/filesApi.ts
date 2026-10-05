import api from './axios';

/** Private files of the business (receipts and service photos), fetched with the session. */
export const filesApi = {
  /** `path` is a private file path ("/files/orders/..."). */
  get: (path: string) => api.get<Blob>(path, { responseType: 'blob' }),
};
