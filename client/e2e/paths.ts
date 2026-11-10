import { fileURLToPath } from 'node:url';

/** Where the tests find the API: the one start-api.mjs starts, or one already running (E2E_BASE_URL and E2E_API_LOG). */
export const usesRunningApi = !!process.env.E2E_BASE_URL;
if (usesRunningApi && !process.env.E2E_API_LOG) {
  // Without its log the tests would read the one of an earlier run and wait for an email that never comes.
  throw new Error('E2E_BASE_URL needs E2E_API_LOG: the log of that API, where it writes the emails it could not send.');
}
export const baseURL = process.env.E2E_BASE_URL ?? `http://127.0.0.1:${process.env.E2E_PORT ?? 5299}`;
export const apiLog = process.env.E2E_API_LOG ?? fileURLToPath(new URL('./.work/api.log', import.meta.url));
export const apiDir = process.env.E2E_API_DIR ?? fileURLToPath(new URL('./.work/api', import.meta.url));
