import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test } from '@playwright/test';

const here = path.dirname(fileURLToPath(import.meta.url));

test('every route of the app has a scene', () => {
  const app = readFileSync(path.resolve(here, '../../src/App.tsx'), 'utf8');
  const routes = [...app.matchAll(/path="([^"]+)"/g)].map((match) => match[1]).filter((route) => route !== '*');
  const opened = readdirSync(here)
    .filter((file) => file.endsWith('.spec.ts'))
    .flatMap((file) => [...readFileSync(path.join(here, file), 'utf8').matchAll(/open\(page, '([^']+)'/g)].map((match) => match[1]));
  const visited = (route: string) => {
    const pattern = new RegExp(`^${route.replace(/:[^/]+/g, '[^/]+')}(\\?.*)?$`);
    return opened.some((url) => pattern.test(url));
  };

  expect(routes.filter((route) => !visited(route)), 'rutas de App.tsx sin escena').toEqual([]);
});
