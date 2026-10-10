import { afterEach, describe, expect, it, vi } from 'vitest';
import { entryScriptOf, isNewVersionDeployed, runningEntryScript } from './newVersion';

describe('newVersion', () => {
  afterEach(() => {
    document.head.innerHTML = '';
  });

  it('finds the script of the build in a copy of index.html', () => {
    const html = '<script type="module" crossorigin src="/assets/index-y0BGjIfm.js"></script>'
      + '<link rel="modulepreload" href="/assets/vendor-react-ZIrH-hyZ.js">';

    expect(entryScriptOf(html)).toBe('/assets/index-y0BGjIfm.js');
    expect(entryScriptOf('<script type="module" src="/src/main.tsx"></script>')).toBeNull();
  });

  it('knows the build of this page, and none in development', () => {
    document.head.innerHTML = '<script type="module" src="/src/main.tsx"></script>';
    expect(runningEntryScript()).toBeNull();

    document.head.innerHTML += '<script type="module" crossorigin src="/assets/index-old111.js"></script>';
    expect(runningEntryScript()).toBe('/assets/index-old111.js');
  });

  it('asks the server for index.html without the cache and compares builds', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response('<script type="module" src="/assets/index-new222.js"></script>'))
      .mockResolvedValueOnce(new Response('<script type="module" src="/assets/index-old111.js"></script>'));

    await expect(isNewVersionDeployed('/assets/index-old111.js')).resolves.toBe(true);
    await expect(isNewVersionDeployed('/assets/index-old111.js')).resolves.toBe(false);
    expect(fetchMock).toHaveBeenCalledWith('/', expect.objectContaining({ cache: 'no-store' }));
  });

  it('takes no page without the app for a new version: a Wi-Fi login or the host restarting', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response('<html><body>Inicia sesión en la red</body></html>'));

    await expect(isNewVersionDeployed('/assets/index-old111.js')).resolves.toBe(false);
  });

  it('takes no error page for a new version, even one that names another build', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      new Response('<script type="module" src="/assets/index-new222.js"></script>', { status: 503 }),
    );

    await expect(isNewVersionDeployed('/assets/index-old111.js')).resolves.toBe(false);
  });
});
