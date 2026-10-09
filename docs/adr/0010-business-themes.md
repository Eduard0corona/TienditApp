# ADR 0010: Temas por negocio con paletas curadas

- Estado: Aceptada
- Fecha: 2026-10-09

## Contexto

Cada negocio veía la app y su menú en línea con el verde de TienditApp, así que el menú de una taquería se veía igual que el de una ferretería. Los dueños quieren su color, y quien atiende varios negocios necesita distinguir de un vistazo en cuál está.

La auditoría de la Fase 16 midió los colores en uso y encontró:

- **Estados de Hyper sin contraste:** debajo de los tokens de TienditApp seguían los colores de estado de la plantilla Hyper, y ninguno pasaba WCAG AA. El verde de "Lista" y "Activo" daba 2.0:1, el rojo de "Cerrar caja" 3.0:1, el amarillo de "Ver mi plan" 1.6:1 y el azul de "Creada por empresa" 2.5:1.
- **Texto general:** quedaba en 4.28:1 sobre el fondo de la página.
- **Modo oscuro:** la variable del fondo de la página se definía para el modo claro y no para el oscuro, así que el fondo se quedaba claro detrás de tarjetas oscuras.

Un color libre que elige el dueño no garantiza nada de eso. Además tendría que funcionar en claro y en oscuro, en botones, enlaces, opciones elegidas y en el menú que ven sus clientes.

## Decisión

1. **El dueño elige entre temas curados, no un color libre.** Hay ocho:
   - Nopal, el verde de TienditApp y el tema por omisión;
   - Talavera, Jacaranda, Rosa mexicano, Jamaica, Cempasúchil, Café de olla y Grafito.

   Cada uno trae su paleta para el modo claro y para el oscuro, y cada par que la app pinta pasa WCAG AA. `businessThemes.test.ts` revisa en cada tema:
   - el texto de los botones en reposo, al pasar el cursor y al presionar;
   - los enlaces sobre las tarjetas y sobre la página;
   - el texto de lo elegido sobre su fondo;
   - el texto general sobre la página;
   - el acento como texto (botones con contorno, textos en el color del negocio) sobre las tarjetas y la página en claro, y sobre las tres superficies de la app en oscuro, igual que los enlaces.

   Nopal conserva los colores claros con los que salió TienditApp. En oscuro, su acento pasó de `#2fb386` al verde claro de sus enlaces (`#5fd0a8`): el anterior pasaba como borde (3:1) pero no como texto.
2. **El negocio guarda cuál, el cliente sabe cómo se ve.**
   - **Backend:** `TenantSettings.Theme` es el enum `BusinessTheme`, guardado como número con Nopal (0) por omisión. Lo cambian dueños y administradores con `PUT /api/v1/auth/my-tenant/theme` (`[RequiresTenantManager]`), y cada cambio queda en la bitácora (`BusinessThemeUpdated`).
   - **Cliente:** los colores viven en `client/src/utils/businessThemes.ts`. Agregar un tema es un valor nuevo del enum más su paleta probada, sin migración.
3. **El tema cambia el acento y el tinte de la página, nunca los colores de estado.** Pendiente, en preparación, listo, cancelado, éxito, aviso y error son los mismos en todos los negocios, para que nadie lea mal un estado después de un cambio de tema. Un estado nunca se pinta con el acento (`bg-primary`); usa `ta-chip-*` o los colores de estado.
4. **Claro u oscuro sigue siendo de cada persona.** Lo elige con la luna de la barra superior, en su dispositivo. El tema de colores es del dueño y aplica a todo su equipo.
5. **Dónde aplica:**
   - en la app del negocio, para todo su equipo, y cambia al cambiar de negocio;
   - en el menú en línea, que sus clientes ven con sus colores aunque en el navegador haya una sesión de otro negocio;
   - en la barra del navegador del teléfono.

   La marca TienditApp (el logo) y los tickets impresos no cambian.
6. **`theme.css` va por capas:**
   - las variables del tema (`--tt-*`, que `utils/theme.ts` pone en `<html>`);
   - los neutros y los colores de estado;
   - el mapeo a Hyper para cada modo.

   El bloque claro no aplica a una página oscura, y el bloque oscuro define todas las variables que define el claro; una prueba lo exige.

## Consecuencias

- **Lo que logra:** los dueños personalizan su app y su menú en un clic, con vista previa en toda la app antes de guardar, sin poder elegir una combinación ilegible. Los colores de estado y el modo oscuro quedaron accesibles para todos los negocios, también para quien no cambie de tema.
- **Lo que limita:** un negocio con un color de marca exacto (un Pantone propio) no puede usarlo todavía. Si se pide, el siguiente paso es un color libre que el servidor y el cliente validen generando la paleta como lo hace hoy el script de la Fase 16, rechazando los que no alcancen AA en los dos modos.
- **Lo que queda pendiente:** Hyper sigue cargado debajo (`app.min.css`, 436 KB) y algunos componentes compilan sus colores. `theme.css` los cubre uno por uno; retirarlo es la siguiente limpieza de UI.
- **Reversibilidad:** quitar la columna `TenantSettings.Theme` (`rollback.sql`) solo pierde la elección; todos vuelven a Nopal.
