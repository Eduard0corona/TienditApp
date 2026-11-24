# Pruebas de extremo a extremo

Recorren TienditApp como lo usa la gente: el cliente que sirve la API, publicada como en producción, contra un SQL Server real, en Chromium. Solo Mercado Pago está simulado (ver «Mercado Pago» abajo): todo lo de TienditApp es real. Desde la Fase 31D ([ADR 0085](../../docs/adr/0085-end-to-end-tests.md)) corren en CI en cada PR (job `End to end`).

Son pocas y largas a propósito: cada una cruza el registro, la API, la base y varias pantallas. Lo que se puede probar por partes va en Vitest (`src/**/*.test.ts(x)`), en las escenas visuales (`../visual`) o en las pruebas del servidor.

## Qué recorren

- `specs/primera-venta.spec.ts`: una dueña registra su taquería, activa su cuenta con el enlace del correo, entra, pasa por Primeros pasos con la plantilla «Taquería», abre su caja desde Vender, cobra dos tacos al pastor en efectivo y da cambio. Pedidos tiene la venta pagada y Mi caja cuenta el efectivo.
- `specs/pedido-con-asistente.spec.ts` (Fase 32C): un asistente de IA, por el servidor MCP (`/mcp`), busca «taco de pastor» y lo encuentra en el índice de Explorar; busca el negocio por su nombre, lee su menú y prepara dos tacos con una nota. El cliente abre el enlace en otro navegador: el carrito dice que lo preparó su asistente y que todavía no le llega al negocio, y lo envía. El asistente ve el folio con `ver_pedido`, el enlace abierto otra vez dice que ya se envió y el negocio tiene el pedido en Pedidos. Espera hasta 75 segundos a que el índice de Explorar (30 segundos) tenga el negocio nuevo.
- `specs/asistente-con-cuenta.spec.ts` (Fase 32E): un cliente con cuenta pide dos tacos en la tienda con su sesión. Su asistente se registra por OAuth (`/oauth/register`) y manda el navegador a autorizar: la pantalla de permiso dice quién lo pide y que regresa a una app de este dispositivo, el cliente lo permite y el navegador regresa con el código (la prueba atiende esa dirección local). El código se canjea con su verificador PKCE y, en `/mcp/cuenta`, `mis_pedidos` trae el pedido y `pedir_otra_vez` lo prepara de nuevo; su enlace llena el carrito. En Mi cuenta el cliente ve el asistente conectado y lo desconecta, y la siguiente petición del asistente recibe 401.
- `specs/pago-en-linea.spec.ts`: el negocio conecta su cuenta de Mercado Pago desde Configuración › Pagos en línea (autoriza en Mercado Pago, regresa con el código y la API lo canjea con su verificador PKCE) y ofrece pagar en línea. Un cliente sin cuenta, en otro navegador, pide un taco al pastor, elige «Pagar ahora en línea» y paga en Mercado Pago; al regresar, la página le dice «¡Pago recibido!» con su folio, porque la API leyó el pago con la llave del negocio y creó el pedido. El negocio lo tiene en Pedidos ya pagado, y volver a la misma página no crea otro.
- `specs/cita-en-linea.spec.ts`: una barbería prende la Agenda y Servicios en Primeros pasos y usa la plantilla «Barbería». Publica su menú, pone su horario y prende las citas en línea. Un cliente sin cuenta, en otro navegador, agenda un corte para mañana a las 10:00 a. m. desde el menú; su enlace dice «Por confirmar». El negocio ve la solicitud en la Agenda y la acepta, y el enlace del cliente dice «Confirmada».
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

## Mercado Pago

Mercado Pago es el único servicio de fuera que se simula: el real necesita la aplicación de TienditApp en Mercado Pago, cuentas que reciben dinero e internet. `fake-mercadopago.ts` lo reemplaza:

- **Lo que llama la API** (token de OAuth, preferencias, pagos y su búsqueda) va a un servidor HTTP que la prueba arranca en `127.0.0.1:5298` (otro con `E2E_MP_PORT`). `start-api.mjs` configura la API con esa dirección, el id de la aplicación que el simulado acepta, secretos nuevos en cada corrida y una llave nueva para cifrar los tokens de las cuentas.
- **Las páginas del navegador** (autorizar y pagar) son de `https://mercadopago.e2e.test`, un dominio que no existe: el cliente solo sale a páginas `https`. `routeMercadoPagoPages` las contesta en cada contexto del navegador como Mercado Pago: el dueño autoriza y el cliente paga con una tarjeta aprobada, al momento.
- **Revisa lo que revisaría Mercado Pago:** el id de la aplicación, el verificador PKCE del código, la llave de la cuenta en cada llamada, y que un pago solo se lea con la llave de la cuenta que lo cobró.
- **Contra una API que ya está corriendo** (`E2E_BASE_URL`), `pago-en-linea.spec.ts` se omite: esa API no llama al simulado.

## Pruebas nuevas

- Van en `specs/<flujo>.spec.ts`, con `test` y `expect` de `@playwright/test` y los ayudantes de `support.ts`: `newOwner`, `signUpBusiness`, `setUpWithTaqueria` y `openDrawerFromSell` (y, para pagar en línea, `startFakeMercadoPago` y `routeMercadoPagoPages`).
- Un flujo que vale la pena aquí cruza la API y varias pantallas, y se rompería sin que lo note una prueba por partes. Lo demás va en Vitest o en el servidor.
- No dependen de la hora: el negocio nuevo no tiene horario (siempre abierto) y las pruebas no leen fechas. La de citas pone el horario de 9 a. m. a 8 p. m. todos los días y agenda mañana a las 10:00 a. m., que siempre está libre.
- Antes del PR, córrelas dos veces seguidas: las dos deben pasar.
