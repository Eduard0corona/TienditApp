/**
 * The app's own page as the server has it now. Not through the API client (axios.ts): it is the site's index.html,
 * not the API, and needs no session or interceptors. Asked without the cache, so it always names the deployed build.
 */
export const appVersionApi = {
  /** The deployed index.html, or null when the server does not answer with it. */
  getDeployedPage: async (): Promise<string | null> => {
    const response = await fetch('/', { cache: 'no-store', headers: { Accept: 'text/html' } });
    return response.ok ? response.text() : null;
  },
};
