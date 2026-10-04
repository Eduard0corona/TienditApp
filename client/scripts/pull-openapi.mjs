// Downloads the API's OpenAPI document (the API must be running in Development) and writes it to
// openapi/openapi.json. Then `npm run api:types` regenerates src/api/generated/schema.ts.
//   node scripts/pull-openapi.mjs [url]
import { writeFile } from 'node:fs/promises';

const url = process.argv[2] ?? 'http://localhost:5000/swagger/v1/swagger.json';
const response = await fetch(url);
if (!response.ok) {
  console.error(`No se pudo descargar ${url}: ${response.status}`);
  process.exit(1);
}
const document = await response.json();
await writeFile(new URL('../openapi/openapi.json', import.meta.url), `${JSON.stringify(document, null, 2)}\n`);
console.log(`openapi/openapi.json actualizado (${Object.keys(document.paths ?? {}).length} rutas).`);
