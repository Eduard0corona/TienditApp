# Reglas del cliente

- Usa `src/api/axios.ts` y módulos `src/api`; no crees otras instancias Axios ni disperses `fetch`.
- React Router/guards controlan UX, no seguridad. Backend sigue siendo autoridad de tenant, permisos y capabilities.
- No calcules de forma autoritativa estados, totales, impuestos, saldos o existencias.
- Mantén TypeScript estricto; no introduzcas `any` sin justificación localizada.
- Los enums del dominio salen del contrato: `npm run api:types` genera `src/api/generated/schema.ts` y `src/api/generated/enums.ts` (no se editan) y `src/types/index.ts` los reexporta. No copies un enum a mano.
- Reutiliza componentes comunes, hooks y tipos; divide componentes grandes solo en refactors focalizados.
- Colores solo con los tokens de `src/assets/css/theme.css` y la paleta del tema del negocio (`utils/businessThemes.ts`): un estado usa `ta-chip-*` o los colores de estado, nunca el acento (`bg-primary`), y la acción principal es `btn-primary`. Un tema nuevo entra con todos sus pares AA en `businessThemes.test.ts` (ADR 0010).
- Textos con mayúscula solo al inicio y en nombres propios, "negocio" (no "empresa") y "correo" (no "email"). Una pantalla tiene una sola acción principal y no repite las de la barra superior o la de pestañas; las migas (`PageTitle` con `parent`) solo van en las pantallas de detalle.
- Una foto que puede faltar (producto, logo) usa `PhotoOrInitial`: la inicial donde falta mientras otras tengan foto. Una fila de chips que se desliza usa `useScrollFade` con `.ta-scroll-fade`.
- Una lista usa `DataTable` con una sola barra: búsqueda, filtros (`filters`, cada uno en `DataTableFilter`) y su acción (`toolbarEnd`); la paginación va abajo. Una acción de fila dice con texto lo que hace, y su `aria-label` empieza con ese texto y nombra el registro; un botón que solo lleva ícono necesita `aria-label`.
- Conserva estados loading, empty, error y success y accesibilidad básica de formularios/controles. El lint incluye `eslint-plugin-jsx-a11y`: cada etiqueta va con `htmlFor`/`id` (con el índice dentro de listas) o envuelve su control, y lo clicable es un `button` o un enlace real (`utils/a11y.ts` `asButton` solo para bloques que no caben en un botón).
- No guardes access/refresh tokens o secretos de integración en almacenamiento accesible a JavaScript. El access token actual permanece en memoria.
- Limpia conexiones SignalR y preserva aislamiento tenant.
- Ejecuta `npm ci`, `npm run lint`, `npm test` y `npm run build`; `package-lock.json` no debe cambiar por validación.
- Las pruebas (Vitest y Testing Library) viven junto al código como `*.test.ts(x)`: utilidades que reflejan reglas de la API, la matriz de acceso y componentes con dinero, existencias o errores.
