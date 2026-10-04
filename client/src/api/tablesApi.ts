import { apiRequest, type Schemas } from './typedClient';

export type DiningRoom = Schemas['DiningRoomDto'];
export type DiningTable = Schemas['DiningTableDto'];
export type OpenTabSummary = Schemas['OpenTabSummaryDto'];

/** Tables of the dining room and their open tabs (businesses with the Food module). */
export const tablesApi = {
  getDiningRoom: (includeInactive = false) =>
    apiRequest('get', '/api/v1/tables', { query: { includeInactive } }),

  create: (body: Schemas['CreateDiningTableCommand']) =>
    apiRequest('post', '/api/v1/tables', { body }),

  update: (id: string, body: Schemas['UpdateDiningTableCommand']) =>
    apiRequest('put', '/api/v1/tables/{id}', { path: { id }, body }),
};
