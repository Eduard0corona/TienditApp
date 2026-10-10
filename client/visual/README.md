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
- **Selectores:** se busca por rol, etiqueta o texto visible, nunca por las clases de la plantilla (`leftside-menu`, `navbar-custom`, `page-title`…). Así la misma escena corre contra `master` y contra la rama que renombra esas clases.
- **Muestrario** (`specimen.ts`): pone en una pantalla real una muestra de los componentes que usa la app (botones, campos, tarjetas, tablas, badges, alertas, menús y las clases que hoy solo da Hyper). Compara su imagen y, en claro, en oscuro y en los ocho temas, dos cosas:
  - el estilo calculado de cada pieza;
  - el valor de cada variable CSS que leen nuestros estilos.

  Un cambio de plantilla que mueve un padding o un color aparece ahí aunque ninguna pantalla lo muestre.
- `scenes/coverage.spec.ts` falla si una ruta de `App.tsx` no tiene escena.

## Escenas nuevas

- Van en `scenes/<grupo>.spec.ts`, con `test` y `expect` de `../fixtures` y los ayudantes de `../helpers`:
  - `open` abre una ruta y espera a que termine de dibujarse;
  - `snap` compara la página completa;
  - `snapElement` compara un elemento;
  - `dialog` busca un diálogo por su nombre (el texto de su título) y espera a que termine de abrir.
- **Respuestas que faltan:**
  - Las respuestas que solo necesita un grupo van en su archivo `scenes/<grupo>.mocks.ts`, con `test.use({ extraRoutes, prepareData })`.
  - Lo que comparten todos va en `mocks/`.
  - La forma de cada respuesta sale de `src/api/generated/schema.ts`.
- **Cambios de diseño a propósito:** el PR los muestra con sus capturas. Después de su merge, la siguiente línea base ya los trae.
