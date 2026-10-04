/**
 * Small IndexedDB store of this device for working without connection: the last catalog of each business and
 * the sales waiting to be sent. Without IndexedDB (some private windows) offline mode is off and sales need the
 * connection, as before.
 */
const DB_NAME = 'tienditapp-offline';
const DB_VERSION = 1;

export const OFFLINE_STORES = {
  catalog: 'catalog',
  sales: 'pendingSales',
} as const;

type StoreName = (typeof OFFLINE_STORES)[keyof typeof OFFLINE_STORES];

let dbPromise: Promise<IDBDatabase> | null = null;

function openDb(): Promise<IDBDatabase> {
  if (typeof indexedDB === 'undefined') return Promise.reject(new Error('IndexedDB is not available'));
  dbPromise ??= new Promise<IDBDatabase>((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(OFFLINE_STORES.catalog)) db.createObjectStore(OFFLINE_STORES.catalog);
      if (!db.objectStoreNames.contains(OFFLINE_STORES.sales)) db.createObjectStore(OFFLINE_STORES.sales, { keyPath: 'id' });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => {
      dbPromise = null;
      reject(request.error ?? new Error('IndexedDB could not be opened'));
    };
  });
  return dbPromise;
}

function run<T>(store: StoreName, mode: IDBTransactionMode, action: (objectStore: IDBObjectStore) => IDBRequest): Promise<T> {
  return openDb().then((db) => new Promise<T>((resolve, reject) => {
    const transaction = db.transaction(store, mode);
    const request = action(transaction.objectStore(store));
    transaction.oncomplete = () => resolve(request.result as T);
    transaction.onerror = () => reject(transaction.error ?? request.error);
    transaction.onabort = () => reject(transaction.error ?? new Error('IndexedDB transaction aborted'));
  }));
}

export const offlineDb = {
  get: <T>(store: StoreName, key: IDBValidKey) => run<T | undefined>(store, 'readonly', (s) => s.get(key)),
  getAll: <T>(store: StoreName) => run<T[]>(store, 'readonly', (s) => s.getAll()),
  /** Stores with the key given (stores without keyPath) or the object's own key. */
  put: (store: StoreName, value: unknown, key?: IDBValidKey) => run<IDBValidKey>(store, 'readwrite', (s) => (key === undefined ? s.put(value) : s.put(value, key))),
  delete: (store: StoreName, key: IDBValidKey) => run<undefined>(store, 'readwrite', (s) => s.delete(key)),
};

export async function isOfflineStoreAvailable(): Promise<boolean> {
  try {
    await openDb();
    return true;
  } catch {
    return false;
  }
}
