# ADR 0085: Pruebas de extremo a extremo contra la API y SQL Server reales

- Estado: Aceptada en la Fase 31D (2026-11-10).
- Fecha: 2026-11-10

## Contexto

- **Cada capa tiene sus pruebas, pero ninguna las junta.** El cliente tiene más de 2,100 pruebas de Vitest con la API simulada. Las escenas visuales (Fase 20B) también simulan la API, con respuestas que se comparan con el contrato (Fase 30D). El servidor prueba sus capas y, en la suite de aislamiento y contra SQL Server, el pipeline HTTP completo en proceso. CI revisa que el cliente se genere del OpenAPI que sirve la API.
- **Lo que queda sin probar es el recorrido.** Ninguna prueba registra un negocio, lo activa con el correo, pasa por Primeros pasos, abre una caja y cobra, ni lleva un pedido del menú en línea hasta que el negocio lo confirma y el cliente lo ve. Un cambio que rompe ese camino entre el cliente y la API (una cookie, una redirección, un estado que una pantalla espera y la API ya no manda) pasa todas las pruebas de hoy.
- **La deuda técnica lo tenía pendiente** desde la Fase 5: «Faltan pruebas de extremo a extremo».

El dueño del producto pidió avanzar este punto sin esperar su respuesta a cada decisión. Tomé las de la Fase 31D con esa autorización. No lleva migración ni cambia la API ni el cliente.

## Decisión

1. **Playwright, en `client/e2e`, con Chromium.** Ya está en el proyecto por las escenas visuales (`@playwright/test` con versión exacta). Las pruebas buscan por rol, etiqueta o texto, como las escenas. Corren una a la vez.
2. **Contra la API publicada como en producción.** `npm run e2e:publish` compila el cliente y corre `dotnet publish`, que lo deja en `wwwroot` igual que la imagen de Docker. Playwright arranca el `dll` (`start-api.mjs`) y espera `/health/ready`.
3. **En Development, contra un SQL Server real.** Development migra la base al arrancar, crea un secreto de JWT para esa corrida, deja la cookie de la sesión sin `Secure` (las pruebas van por http) y escribe en su log los correos que no pudo mandar. Así no hace falta ningún secreto ni código solo para pruebas.
   - **Nada de la configuración de quien las corre.** `start-api.mjs` deja fuera los user-secrets de la API y la copia de `appsettings.Development.local.json` que deja el publish, y apaga el correo, los SMS y la telemetría (Application Insights y OTLP) aunque estén en el ambiente. La cadena de conexión lleva MARS, como las de la app.
   - **Lo que cambia contra producción**, por ser Development:
     - la cookie de la sesión es `SameSite=Lax` y sin `Secure` (en producción, `Strict` y `Secure`);
     - no hay HSTS;
     - se sirve Swagger y CORS admite `http://localhost:5173`;
     - la base se crea y se migra al arrancar con EF Core, no con el `apply.sql` de producción (eso lo prueba el job de migraciones);
     - los límites de peticiones del registro, el inicio de sesión y los pedidos públicos son más altos; el general de la API es el mismo;
     - los correos y los SMS que no se mandan se escriben completos en el log.
4. **El enlace de activación sale del log.** Las pruebas leen el correo que Development no pudo mandar, después de «Email not sent to <correo>», y abren su enlace. No hay un endpoint de pruebas en la API ni un proveedor de correo falso.
5. **En CI, en cada PR y en cada push a `master`.** Un check más del PR, como los demás jobs de CI. Un job nuevo (`End to end (Playwright, API and SQL Server)`) corre junto a los demás: levanta SQL Server 2022 con la misma imagen fijada por digest que el job de migraciones y una contraseña creada y enmascarada en el job, publica, instala Chromium de la versión de `@playwright/test` y corre las pruebas. Corre en `ubuntu-24.04`: en `ubuntu-latest` (26.04) Playwright 1.56.1 no instala Chromium; se mueve junto con `@playwright/test`. Si falla, sube el reporte, la traza de cada prueba que falló y el log de la API. Ese log no lleva la cadena de conexión ni la contraseña; lleva los datos de prueba (correos inventados y enlaces de activación de esa corrida). El despliegue no las corre: se revisan en el PR, antes del merge.
6. **Una base por máquina.** Localmente la base `TienditAppE2E` se reusa: cada prueba registra su negocio con un correo de `example.com` (reservado para ejemplos) y un nombre nuevos. En CI la base es nueva en cada corrida. La API solo migra un SQL Server local (la regla de Development que evita migrar uno remoto por error), y las pruebas no la cambian.
7. **Dos recorridos para empezar:**
   - la primera venta: registro, activación, entrar, Primeros pasos con la plantilla «Taquería», abrir la caja desde Vender y cobrar en efectivo con cambio; la venta en Pedidos y el efectivo en Mi caja;
   - el pedido en línea: prender el menú con pedidos, el cliente pide sin cuenta desde otro navegador y abre su seguimiento; el negocio lo ve por confirmar y por cobrar, lo confirma, y el seguimiento lo dice.

   Un recorrido entra aquí si cruza la API y varias pantallas y se puede romper sin que lo note una prueba por partes. Lo demás sigue en Vitest, las escenas o el servidor.

## Consecuencias

- **Lo que logra:** un cambio que rompe el registro, la activación, Primeros pasos, la caja, el cobro, el menú en línea, Pedidos o el seguimiento falla en el PR, aunque cada parte pase sus pruebas. CI tarda lo mismo de principio a fin: el job corre en paralelo y dura menos que el de migraciones.
- **Lo que limita:**
  - **Development no es producción:** lo que solo cambia ahí (la cookie `Strict`, HSTS, `apply.sql`) no lo ven estas pruebas.
  - Cambiar un texto o una etiqueta que estas pruebas usan las rompe. Se actualizan en el mismo PR, como las escenas visuales.
  - Correrlas localmente pide .NET, Chromium y un SQL Server local; el README de `client/e2e` dice cómo, también contra una API que ya está corriendo.
  - Una base local reusada entre ramas con migraciones distintas puede quedar adelante de la rama: cada rama puede usar la suya (`E2E_DATABASE`).
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - Development con el log del correo, en vez de un proveedor de correo falso o un endpoint de pruebas;
  - correrlas en cada PR, no solo antes de desplegar;
  - Chromium y escritorio solamente, y estos dos recorridos para empezar.

## Fuera de esta fase

- Firefox, Safari y el tamaño de un teléfono.
- Pagos en línea (Mercado Pago), timbrado de CFDI, WhatsApp y el contenido de los correos.
- Lo que llega al momento por SignalR: el seguimiento se recarga para leer el cambio, porque lo pregunta cada 20 segundos.
- Varias sucursales, equipo con permisos, plataformas de reparto, entregas a domicilio y citas.
- Correrlas contra el entorno de producción después de desplegar.
