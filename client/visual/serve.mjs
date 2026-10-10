// Serves a build of the client (`node visual/serve.mjs <dist> <port>`) as the API does: the file when it exists,
// otherwise index.html (a screen of the app). Only for the visual scenes; nothing is compressed or cached.
import { createServer } from 'node:http';
import { existsSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';

const [dist = 'dist', port = '4317'] = process.argv.slice(2);
const root = path.resolve(dist);
const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf', '.eot': 'application/vnd.ms-fontobject',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.webp': 'image/webp', '.json': 'application/json', '.ico': 'image/x-icon',
};

if (!existsSync(path.join(root, 'index.html'))) {
  console.error(`No hay build en ${root}: corre npm run build.`);
  process.exit(1);
}

createServer((request, response) => {
  const pathname = decodeURIComponent(new URL(request.url ?? '/', 'http://localhost').pathname);
  const file = path.join(root, pathname);
  const inside = file.startsWith(root + path.sep);
  const target = inside && path.extname(file) && existsSync(file) && statSync(file).isFile() ? file : null;
  if (!target && path.extname(pathname)) {
    response.writeHead(404).end();
    return;
  }
  const served = target ?? path.join(root, 'index.html');
  response.writeHead(200, { 'Content-Type': TYPES[path.extname(served)] ?? 'application/octet-stream' });
  response.end(readFileSync(served));
}).listen(Number(port), '127.0.0.1', () => console.log(`visual: ${root} en http://127.0.0.1:${port}`));
