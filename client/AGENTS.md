# Reglas del cliente

- Usa `src/api/axios.ts` y módulos `src/api`; no crees otras instancias Axios ni disperses `fetch`.
- React Router/guards controlan UX, no seguridad. Backend sigue siendo autoridad de tenant, permisos y capabilities.
- No calcules de forma autoritativa estados, totales, impuestos, saldos o existencias.
- Mantén TypeScript estricto; no introduzcas `any` sin justificación localizada.
- Reutiliza componentes comunes, hooks y tipos; divide componentes grandes solo en refactors focalizados.
- Conserva estados loading, empty, error y success y accesibilidad básica de formularios/controles.
- No guardes access/refresh tokens o secretos de integración en almacenamiento accesible a JavaScript. El access token actual permanece en memoria.
- Limpia conexiones SignalR y preserva aislamiento tenant.
- Ejecuta `npm ci`, `npm run lint` y `npm run build`; `package-lock.json` no debe cambiar por validación.
