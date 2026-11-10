# Pruebas de extremo a extremo

Recorren TienditApp como lo usa la gente: el cliente que sirve la API, publicada como en producción, contra un SQL Server real, en Chromium. No hay nada simulado. Desde la Fase 31D ([ADR 0085](../../docs/adr/0085-end-to-end-tests.md)) corren en CI en cada PR (job `End to end`).

Son pocas y largas a propósito: cada una cruza el registro, la API, la base y varias pantallas. Lo que se puede probar por partes va en Vitest (`src/**/*.test.ts(x)`), en las escenas visuales (`../visual`) o en las pruebas del servidor.

## Qué recorren

- `specs/primera-venta.spec.ts`: una dueña registra su taquería, activa su cuenta con el enlace del correo, entra, pasa por Primeros pasos con la plantilla «Taquería», abre su caja desde Vender, cobra dos tacos al pastor en efectivo y da cambio. Pedidos tiene la venta pagada y Mi caja cuenta el efectivo.
- `specs/pedido-en-linea.spec.ts`: el negocio abre su caja y prende el menú con pedidos. Un cliente, en otro navegador y sin cuenta, pide desde `/m/<negocio>` y abre el seguimiento. El negocio ve el pedido en Pedidos, por confirmar y por cobrar, y lo confirma; el seguimiento del cliente lo dice.

## Cómo se corren

Necesitan .NET 10, Chromium y un SQL Server local. Desde `client/`, en PowerShell:

```powershell
npm run e2e:publish   # compila el cliente y publica la API con él en e2e/.work/api
$env:E2E_SQLSERVER = 'Server=localhost,1433;User ID=sa;Password=…;Encrypt=True;TrustServerCertificate=True'
npm run e2e
npx playwright show-report e2e/.work/report   # lo que pasó, con la traza de cada prueba que falló
```

En bash, `E2E_SQLSERVER='…' npm run e2e`.

- `E2E_SQLSERVER` es la cadena de conexión sin `Database`, como `TIENDITAPP_TEST_SQLSERVER` de las pruebas del servidor. La base es `TienditAppE2E` (otra con `E2E_DATABASE`): la API la crea y la migra al arrancar, como en Development. Se reusa entre corridas: cada prueba registra su propio negocio, con un correo de `example.com` y un nombre nuevos. Si dos ramas tienen migraciones distintas, dale a cada una su base con `E2E_DATABASE`.
- El servidor tiene que ser local (`localhost`, `127.0.0.1`, LocalDB o un nombre sin puntos, como el de un servicio de docker-compose): Development no migra uno remoto, y `/health/ready` no contestaría. Si la API no arranca, `e2e/.work/api.log` dice por qué.
- La API corre en `http://127.0.0.1:5299` (otro puerto con `E2E_PORT`) y escribe todo en `e2e/.work/api.log`.
- Con Chromium en otra ruta: `E2E_CHROMIUM=/ruta/al/chromium npm run e2e`. Sin él, el de `npx playwright install chromium`.
- Contra una API que ya está corriendo, en Development y sin proveedor de correo: `E2E_BASE_URL` con su dirección y `E2E_API_LOG` con la ruta de su log (sin él no arrancan). Así no se publica ni se arranca otra.
- Después de cambiar el cliente o la API, publica otra vez: las pruebas usan lo que hay en `e2e/.work/api`.
- La API publicada, el log, los reportes y las trazas (`e2e/.work`) no entran a git.

## Cómo funcionan

- `playwright.config.ts` arranca la API con `start-api.mjs`: el `dll` publicado en Development, con `AppUrl` en la dirección de las pruebas, y espera a que `/health/ready` conteste. Development usa un secreto de JWT de esa corrida y migra la base; no hace falta ningún secreto más.
- **Nada de tu configuración:** `start-api.mjs` deja fuera tus user-secrets de la API y la copia de `appsettings.Development.local.json` que deja el publish, y apaga el correo, los SMS y la telemetría aunque estén en tu ambiente. La cadena de conexión lleva MARS, como las de la app.
- Un paso que no puede pasar falla a los 15 segundos diciendo cuál (`actionTimeout`), no al límite de la prueba.
- **El correo de activación sale del log.** Sin proveedor de correo, Development escribe cada correo que no pudo mandar después de «Email not sent to <correo>». `activationLink` (`support.ts`) espera esa línea y toma el primer enlace de confirmación que la sigue, así nunca toma el de otro negocio.
- Corren una a la vez (`workers: 1`): la API es un solo proceso y cada prueba ya cruza varias pantallas.
- **Selectores:** por rol, etiqueta o texto visible, como las escenas visuales; nunca por clases. Una tarjeta sin nombre propio (la plantilla, una cifra de Mi caja) se encuentra como el bloque más chico que tiene su texto y su botón o su monto.
- Un cliente en otro dispositivo es otro contexto del navegador (`browser.newContext`), con su propio almacenamiento y sin la sesión del negocio.

## Pruebas nuevas

- Van en `specs/<flujo>.spec.ts`, con `test` y `expect` de `@playwright/test` y los ayudantes de `support.ts`: `newOwner`, `signUpBusiness`, `setUpWithTaqueria` y `openDrawerFromSell`.
- Un flujo que vale la pena aquí cruza la API y varias pantallas, y se rompería sin que lo note una prueba por partes. Lo demás va en Vitest o en el servidor.
- No dependen de la hora: el negocio nuevo no tiene horario (siempre abierto) y las pruebas no leen fechas.
- Antes del PR, córrelas dos veces seguidas: las dos deben pasar.
