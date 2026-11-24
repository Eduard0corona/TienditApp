// Starts the published API for the end-to-end tests (README.md): Development, on the tests' address, against its own
// database in the SQL Server of E2E_SQLSERVER. Everything it writes goes to the log the tests read: without an email
// provider, Development writes the email it could not send, and the activation link comes from there.
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { createWriteStream, existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import path from 'node:path';

const [url, apiDir, logFile] = process.argv.slice(2);
const server = process.env.E2E_SQLSERVER?.trim();
if (!server) {
  console.error('E2E_SQLSERVER: the connection string of the SQL Server for the tests, without Database (README.md).');
  process.exit(1);
}
const dll = path.resolve(apiDir, 'FoodParkDigital.WebApi.dll');
if (!existsSync(dll)) {
  console.error(`${dll} does not exist: publish the API first (npm run e2e:publish).`);
  process.exit(1);
}
// One database for every run: each test signs up its own business, so what earlier runs left does not get in the way.
const database = process.env.E2E_DATABASE?.trim() || 'TienditAppE2E';
const work = path.dirname(path.resolve(logFile));

// Nothing of the developer's own setup: the publish copies appsettings.Development.local.json if there is one (the copy
// next to the dll goes, never the file of the project), and Development reads the user-secrets of the API, found under
// APPDATA first on every system. With a real email provider the activation email would leave the machine instead of
// reaching the log.
for (const file of readdirSync(path.dirname(dll))) {
  if (/^appsettings\..+\.local\.json$/i.test(file)) rmSync(path.join(path.dirname(dll), file));
}
const noUserSecrets = path.join(work, 'no-user-secrets');
mkdirSync(noUserSecrets, { recursive: true });
// Mercado Pago is the stand-in of e2e/fake-mercadopago.ts: the API calls it on this port, and the browser goes to its pages,
// which the test answers. The application's id is the one the stand-in accepts; the secrets are new on each run, and the key
// that protects the accounts' tokens too (an account connected in an earlier run then reads as without a key).
const mercadoPagoPort = Number(process.env.E2E_MP_PORT ?? 5298);

const log = createWriteStream(logFile, { flags: 'w' });
const api = spawn('dotnet', [dll], {
  cwd: path.dirname(dll),
  stdio: ['ignore', 'pipe', 'pipe'],
  env: {
    ...process.env,
    APPDATA: noUserSecrets,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: url,
    AppUrl: url,
    // As the app's connection strings come (docs/architecture/data-consistency-and-domain-events.md).
    ConnectionStrings__DefaultConnection: `${server.replace(/;+$/, '')};Database=${database};MultipleActiveResultSets=True`,
    MercadoPago__ClientId: 'tienditapp-e2e',
    MercadoPago__ClientSecret: randomBytes(24).toString('hex'),
    MercadoPago__WebhookSecret: randomBytes(24).toString('hex'),
    MercadoPago__Sandbox: 'false',
    MercadoPago__ApiBaseUrl: `http://127.0.0.1:${mercadoPagoPort}`,
    MercadoPago__AuthorizationUrl: 'https://mercadopago.e2e.test/authorization',
    SecretProtection__Key: randomBytes(32).toString('base64'),
    EmailSettings__Provider: 'None',
    SmsSettings__Provider: 'None',
    APPLICATIONINSIGHTS_CONNECTION_STRING: '',
    OTEL_EXPORTER_OTLP_ENDPOINT: '',
  },
});
api.stdout.pipe(log, { end: false });
api.stderr.pipe(log, { end: false });
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => api.kill(signal));
// After its output closes, so the log keeps what it wrote last (the error of a failed start).
api.on('close', (code, signal) => {
  const exitCode = code ?? (signal ? 1 : 0);
  if (exitCode !== 0) console.error(`The API stopped (${code ?? signal}); its log is ${logFile}.`);
  log.end(() => process.exit(exitCode));
});
