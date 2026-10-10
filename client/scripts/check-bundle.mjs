// The weight of what every first visit downloads before the first screen, against bundle-budget.json: the global CSS
// and the scripts index.html loads. Run after `npm run build` (CI and the deploy do). A phase that makes them grow
// raises the budget in its PR, saying why; one that shrinks them lowers it, so the next one cannot undo it quietly.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const client = fileURLToPath(new URL('..', import.meta.url));
const dist = path.join(client, 'dist');
const budget = JSON.parse(readFileSync(path.join(client, 'bundle-budget.json'), 'utf8'));
const html = readFileSync(path.join(dist, 'index.html'), 'utf8');

const gzipped = (file) => gzipSync(readFileSync(path.join(dist, file)), { level: 9 }).length;
const linked = (pattern) => [...html.matchAll(pattern)].map((match) => match[1].replace(/^\//, ''));

const measures = {
  globalCssGzip: linked(/<link[^>]+rel="stylesheet"[^>]+href="([^"]+\.css)"/g),
  initialJsGzip: [...linked(/<script[^>]+src="([^"]+\.js)"/g), ...linked(/<link[^>]+rel="modulepreload"[^>]+href="([^"]+\.js)"/g)],
};

let over = false;
for (const [name, files] of Object.entries(measures)) {
  const size = files.reduce((total, file) => total + gzipped(file), 0);
  const limit = budget[name];
  const ok = size <= limit;
  over ||= !ok;
  console.log(`${ok ? 'ok  ' : 'OVER'} ${name}: ${size.toLocaleString('en-US')} B of ${limit.toLocaleString('en-US')} B (${files.length} files)`);
}

if (over) {
  console.error('::error::The first visit grew past client/bundle-budget.json. Shrink it, or raise the budget in this PR and say why.');
  process.exitCode = 1;
}
