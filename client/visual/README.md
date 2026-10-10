# Escenas visuales del cliente

Capturas de cada pantalla de TienditApp para comparar un cambio contra `master`. En la Fase 20 se cambia la plantilla (Hyper) por un CSS propio sin que nada se mueva en pantalla: estas escenas lo comprueban con cero píxeles de tolerancia.

No corren en CI: se corren en la máquina de quien hace el cambio, antes de abrir el PR, y el PR dice el resultado. CI sí revisa que compilen (`npm run visual:typecheck`).

## Cómo se usan

Desde `client/`:

```bash
npm run visual:baseline      # compila origin/master aparte y toma sus capturas (la línea base)
npm run visual:compare       # compila este árbol y compara cada escena con la línea base
npx playwright show-report visual/.work/report   # las diferencias, con la imagen de cada lado
```

- `visual:baseline` acepta otra referencia: `npm run visual:baseline -- origin/mi-rama`. Si el `package-lock.json` es el mismo, reusa estos `node_modules`; si no, instala los de esa referencia.
- Lo que va después de la referencia llega a Playwright: `npm run visual:compare -- --grep Vender`.
- Con Chromium en otra ruta: `VISUAL_CHROMIUM=/ruta/al/chromium npm run visual:compare`.
- Las capturas (`visual/.snapshots`), las compilaciones y los reportes (`visual/.work`) no entran a git.

## Cómo funcionan

- Corren sobre el build de producción (`npm run build`), servido por `serve.mjs` como lo sirve la API: el archivo si existe, si no `index.html`.
- La API se contesta con los datos de `mocks/` (Café La Esquina, la dueña Mariana Ortiz). Todas las escenas pasan el mismo día: 2026-10-04 13:25 en la Ciudad de México.
- **Estrictas:**
  - Una petición sin respuesta simulada hace fallar la escena: si no, se vería como una pantalla vacía que parece correcta.
  - Un error de JavaScript en la página también la hace fallar.
- **Selectores:** se busca por rol, etiqueta o texto visible, nunca por las clases de la plantilla ni del marco (`leftside-menu`, `navbar-custom`, `page-title`… y desde la Fase 20C las `ta-*` de `shell.css`). Así la misma escena corre contra `master` y contra la rama que renombra esas clases.
- **Muestrario** (`specimen.ts`): pone en una pantalla real una muestra de los componentes que usa la app (botones, campos, tarjetas, tablas, badges, alertas, menús y las clases que hoy solo da Hyper). Compara su imagen y, en claro, en oscuro y en los ocho temas, dos cosas:
  - el estilo calculado de cada pieza;
  - el valor de cada variable CSS que leen nuestros estilos.

  Un cambio de plantilla que mueve un padding o un color aparece ahí aunque ninguna pantalla lo muestre.
- `scenes/coverage.spec.ts` falla si una ruta de `App.tsx` no tiene escena.
- **Iguales en cada corrida:** con cero de tolerancia, una escena que cambia sola es una escena que miente. Por eso:
  - no hay transiciones ni animaciones CSS (`fixtures.ts`). Un menú que se desliza o un diálogo que aparece poco a poco se dibujaba a medio camino, y lo que quedaba en pantalla dependía de qué tan ocupada estaba la máquina. Cambia el camino, nunca a dónde llega;
  - `settle` espera además a que terminen las animaciones finitas que queden (las de JavaScript);
  - Chromium repinta mosaicos completos (`--disable-partial-raster` en `playwright.config.ts`): al repintar solo la parte que cambió, los bordes de esa parte salían un tono distinto de una captura a otra;
  - el texto se suaviza en gris (`--disable-lcd-text`): con subpíxeles de color, Chromium elige uno u otro según la capa en que cae el texto, y las capas cambian con marcado que no dibuja nada (un contenedor de más o de menos), no con el diseño;
  - lo que depende de dónde se sirven las escenas (el puerto en una URL) va enmascarado (`snap(page, nombre, { mask: [...] })`).

  Antes de abrir un PR de escenas, corre cada archivo dos veces seguidas contra el mismo build: las dos deben pasar.

## Escenas nuevas

- Van en `scenes/<grupo>.spec.ts`, con `test` y `expect` de `../fixtures` y los ayudantes de `../helpers`:
  - `open` abre una ruta y espera a que termine de dibujarse;
  - `snap` compara la página completa;
  - `snapElement` compara un elemento;
  - `dialog` espera a que un diálogo termine de abrir.
- **Un diálogo** se captura del tamaño de la pantalla: `snap(page, nombre, { fullPage: false })`. Su fondo oscuro cubre la pantalla, no la página: en una captura de página completa, lo que queda abajo saldría sin cubrir.
- **Respuestas que faltan:**
  - Las respuestas que solo necesita un grupo van en su archivo `scenes/<grupo>.mocks.ts`, con `test.use({ extraRoutes, prepareData })`.
  - Una función como valor de una opción va con `asOption` de `../fixtures`: Playwright toma una función suelta en `test.use()` por un fixture. Por ejemplo, `test.use({ prepareData: asOption((data) => { data.tenant.status = 1; }) })`.
  - `extraRoutes` recibe `ok` y `paginate` (una página de lista como la arma la API).
  - Lo que comparten todos va en `mocks/`.
  - La forma de cada respuesta sale de `src/api/generated/schema.ts`.
- **Cambios de diseño a propósito:** el PR los muestra con sus capturas. Después de su merge, la siguiente línea base ya los trae.
- **Dos corridas a la vez** (por ejemplo, dos grupos en paralelo) necesitan su propia carpeta de resultados: `npx playwright test --config visual/playwright.config.ts --output visual/.work/results-<grupo> …`. Playwright vacía la carpeta al empezar, y una corrida borraba lo que la otra estaba revisando.
