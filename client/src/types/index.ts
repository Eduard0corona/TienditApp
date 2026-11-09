// The manual types of the client, one file per context. Every file is re-exported here: the code imports from `types`,
// never from one of the files. The enums come from the API contract (api/generated/enums); a context re-exports the
// ones its screens use.

export * from './common';
export * from './account';
export * from './tenant';
export * from './whatsapp';
export * from './catalog';
export * from './inventory';
export * from './purchasing';
export * from './cash';
export * from './appointments';
export * from './reports';
export * from './messaging';
export * from './customers';
export * from './orders';
export * from './aiAgents';
export * from './quotes';
export * from './invoicing';
