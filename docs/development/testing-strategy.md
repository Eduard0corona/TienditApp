# Estrategia de pruebas

## Estado actual

Existen cuatro proyectos xUnit y las pruebas del cliente:

- `FoodParkDigital.Domain.Tests`: máquinas de estado, cancelación/pagos de órdenes, caja, producto, configuración de WhatsApp, catálogo de permisos, valores persistidos de los enums de inventario, el refresh token (solo su hash, el mismo que calcula la migración de la Fase 9, y el motivo de revocación: `RefreshTokenTests`) y el rol por defecto sin privilegios (`UserRoleTests`, Fase 9).
- `FoodParkDigital.Application.Tests`: casos de órdenes/clientes, capabilities, tenant access, la regla de cada trabajo (`UseCaseRulesTests` y `AuthorizationBehaviorTests`, Fase 5), inventario, compras/caja, reportes, uploads, WhatsApp, empleados, aislamiento de archivos por tenant, archivos privados (fotos de servicio solo del negocio y descarga solo por su negocio y en su área: `PrivateOrderFilesTests`, Fase 8), sesiones revocables (rotación, reuso que termina la sesión, carrera perdida por un momento, cierre de sesión, contraseña nueva, security stamp, cuenta inactiva y un token sin hash que ya no renueva: `UserSessionTests`, Fase 8), el login que no enumera cuentas ni revela el bloqueo (`LoginCommandTests`, Fase 9), la recuperación por SMS con un solo mensaje para cuenta inexistente, código incorrecto o cuenta bloqueada y los códigos enviados después de la petición (`AccountRecoveryTests` y `ResendPhoneConfirmationCommandTests`, Fase 10; desde la Fase 11, la petición guarda la misma solicitud en el outbox exista o no la cuenta, y el handler no envía una solicitud tardía y devuelve el minuto si el proveedor falla), avatares en la carpeta de la cuenta y fotos anteriores borradas solo después de guardar la nueva (`TenantFileIsolationTests` y `UploadProductImageCommandTests`, Fase 9), los topes por negocio de categorías (también al instalar una plantilla), almacenes (también el de una sucursal nueva) y agentes de IA, que no cuentan los de otro negocio, y la cocina con los pedidos más recientes (`PerBusinessLimitsTests`, Fase 12), las rondas de una cuenta de mesa con el servicio de inventario real (descuento al mandarse sin repetir al cerrar, rechazo o aviso según la política, cuenta abierta antes del cambio, cancelación y devolución: `TabRoundStockTests`, Fase 14), el periodo de Inicio (`GetDashboardMetricsQueryTests`), configuración de arranque y EF mediante SQLite/InMemory y dobles. `SqliteTestDatabase` cubre constraints relacionales (índices únicos) que InMemory ignora; no reemplaza a SQL Server. `Support/SqliteTestHost` arma SQLite en memoria con FKs activas, una estrategia de ejecución con reintentos, Identity real con las opciones de producción e inyección de fallos (commit transitorio, comando fallido, escritura concurrente) para flujos transaccionales; no sustituye pruebas contra SQL Server. Ambos emulan el `rowversion` de `WhatsAppConversation` (valor aleatorio al insertar y trigger al actualizar), así que guardar una copia desactualizada falla como en SQL Server.
- `FoodParkDigital.Infrastructure.Tests`: servicios de Infrastructure con dobles (directorio temporal, HTTP, SMTP en loopback, logger, EF InMemory y `SqlException` construidas por reflexión con mensajes reales de SQL Server); cubre el confinamiento por tenant del almacenamiento local y de la resolución de blobs de Azure, el almacenamiento privado local (fuera de `wwwroot`, que rechaza una raíz configurada dentro de él y mueve una sola vez los archivos subidos cuando eran públicos), y que SMS, correo, WhatsApp y la resolución del cliente de un pedido no registren códigos OTP, cuerpos, respuestas de proveedores ni PII fuera de Development. `OutboxTests` prueba desde la Fase 11 que un evento agregado sin entidad (`IOutbox`) se guarda sin negocio, despierta al despachador y llega a su handler. `QuerySplittingTests` (Fase 12) comprueba que el contexto registrado lee cada colección en su propia sentencia. `DatabaseErrorLoggingTests` usa el registro de `AddInfrastructureServices` con un interceptor que responde en lugar de SQL Server para comprobar que EF Core no registra los mensajes de guardados y consultas fallidos fuera de Development.
- `FoodParkDigital.WebApi.Tests`: middleware de tenant (membresía, suspensión y rutas del negocio por defecto), acceso a hubs, controller de órdenes por tenant, verificación de firma del webhook de WhatsApp y su controller, el bloqueo de `/uploads/orders` (`PrivateUploadsGuardMiddlewareTests`, Fase 8), el barrido que cierra conexiones de los hubs (`HubAccessSweeperTests`, Fase 9) y `GlobalExceptionHandlerMiddleware` (errores de base de datos registrados sin mensajes fuera de Development, `traceId` en `ProblemDetails` y, desde la Fase 10, los rechazos esperados como advertencia sin la traza), sin pipeline HTTP completo. `TenantResolutionMiddlewareTests` también prueba, desde la Fase 10, que el acceso de soporte de `SUPER_ADMIN` se registra antes de ejecutarse, que no se ejecuta si no se puede registrar (503) o si el negocio no existe (404), y que no se registra en su propio negocio ni en rutas de la cuenta. Comparte dobles de `Infrastructure.Tests/Support` enlazando sus archivos. `ClientEnumContractTests` lee `client/src/api/generated/enums.ts` (generado del contrato desde la Fase 7) y `client/src/types/index.ts`, y compara cada enum del dominio con su constante en el cliente, porque la API los intercambia como números; `EnumNamesSchemaFilterTests` comprueba que el contrato nombra cada valor. `ArchitectureTests` (Fase 4F) verifica dependencias entre capas, handlers en Application, controllers sin DbContext y sin roles (Fase 5), el filtro de negocio de cada entidad (desde la Fase 8, toda entidad con `TenantId`), que los comandos que toman folio corran en una transacción y, desde la Fase 11, que toda acción reciba el `CancellationToken` y toda llamada al mediador lo pase; `RequestCancellationTests` prueba que una lectura recibe la cancelación de la petición y un cambio ninguna, también al enlazar los parámetros de un controller real y en la API registrada; `GlobalExceptionHandlerMiddlewareTests`, que una petición abandonada por su cliente termina con 499 como Information y un timeout de un proveedor sigue siendo un 500; `ApiProblemContractTests`, que todo error sea `ProblemDetails` con `code`. Desde la Fase 8, `CrossTenantHttpSqlServerTests` sí corre el pipeline HTTP completo (`Microsoft.AspNetCore.Mvc.Testing`) contra SQL Server: dos negocios registrados por la API y el dueño de uno intentando leer, cambiar y borrar por id lo del otro (catálogo, clientes, proveedores, órdenes con su estado y pagos, archivos privados y, desde la Fase 9, inventario, compras, caja y citas) o entrar con su encabezado, su ruta o el cambio de negocio. `CrossTenantRealtimeSqlServerTests` (Fase 9) conecta a los hubs por WebSockets (`Microsoft.AspNetCore.SignalR.Client`) y manda entregas firmadas al webhook de WhatsApp: los eventos llegan solo al negocio dueño, una conexión se cierra al terminar su sesión y cada entrega llega solo al negocio de su número. `CrossTenantManagementSqlServerTests` (Fase 10) extiende la matriz a mesas y cuentas abiertas (entre dos negocios de comida), sucursales, equipo, campañas (plantillas, campañas, bajas y audiencias) y reportes; y `CrossTenantHttpSqlServerTests` inicia sesión como un admin de plataforma real y comprueba que lo que hace en otro negocio queda en la actividad de ese negocio y no en la de los demás. `CrossTenantOperationsSqlServerTests` (Fase 11) cubre Inicio (otro negocio vende, se atrasa con un servicio, se queda corto de un insumo y tiene un chat sin leer, y las métricas y "Requiere atención" de B no se mueven), la cocina entre dos negocios de comida, el tablero de servicios y los agentes de IA; cada caso repite la acción con lo suyo para que el rechazo sea por el negocio. Desde la Fase 12, `CrossTenantHttpSqlServerTests` también intenta reembolsar y anular una orden del otro y armar recetas con sus productos, insumos o almacenes, y lee la base con el filtro del negocio (`TwoBusinessesApi.QueryAsync`) para comprobar que el rechazo no dejó filas; `CrossTenantManagementSqlServerTests` manda una promoción rápida que llega solo a los compradores del negocio sin tocar la plantilla del otro. Desde la Fase 13, la suite comprueba que las rutas de la cuenta (`/me`) solo muestran los negocios, el acceso y las compras propios, y que la lista de todos los negocios y crear uno responden 403 a un dueño; `CrossTenantSuiteCoverageTests` lee las URLs de la suite y falla si un controller con datos del negocio no aparece en ellas (cada URL cuenta para el controller con la ruta más larga que la cubre; los que quedan fuera dicen por qué). Desde la Fase 14 cuenta cada URL por la ruta de cada acción, porque varios controllers comparten `/api/v1/auth` y `/api/v1/auth/my-tenant`; con eso la suite prueba también la configuración del negocio (`TenantSetupController`), que antes contaba solo por el prefijo. `AccountEndpointsTests` (Fase 14) fija las rutas de la cuenta (las 33 que eran de `AuthController` y, desde la 24E, las dos de la portada del menú), con su método, quién puede llamarlas y su límite de peticiones. `TabRoundStockSqlServerTests` (Fase 14) manda rondas de una cuenta de mesa por la API: el rechazo por falta de existencias (422) no deja nada ni consume folio, el aviso viaja en la respuesta, el cierre no descuenta dos veces y la cancelación devuelve todo. Las clases comparten la API y la base (`TwoBusinessesApi`, una colección de xUnit). Las de las Fases 10 a 13 se validaron con mutaciones: quitar el filtro del negocio en un handler de cada área las hace fallar; al cancelar una campaña, mover un pedido de cocina, cambiar el estado de una pieza o reembolsar lo frena además `TenantSaveChangesInterceptor` (sus filas nuevas llevan el negocio ajeno), y sin las dos capas la suite lo detecta.
- Cliente (Fase 5): Vitest con jsdom y Testing Library (`npm test`, archivos `*.test.ts(x)` junto al código): la matriz de acceso (`access.ts`), dinero y cantidades, la estimación de totales con IVA que replica a la API, los mensajes de error por `code`, la tarjeta de existencias del producto con la API simulada las fotos privadas, que se piden con la sesión y se muestran desde la memoria del navegador (`PrivatePhoto`, `privateFiles`, Fase 8), y qué token sirve para conectar los hubs (`tokenClaims`, Fase 8).
- Pruebas contra SQL Server 2022 (Fase 4F): las clases `*SqlServerTests` de `Infrastructure.Tests` corren en el job `Migrations` contra el contenedor (`TIENDITAPP_TEST_SQLSERVER`) y se omiten en el resto: contadores de folios con solicitudes simultáneas, la traducción de las proyecciones de listas (`DtoProjectionSqlServerTests`) las cifras de Inicio, que desde la Fase 14 se calculan en SQL y con el resumen diario y dan lo mismo que cargar el rango (`DashboardMetricsSqlServerTests`), y la venta como transacción con la configuración de producción (reintentos y MARS): folio devuelto al rechazarla, aviso en tiempo real después del commit y la copia tardía de una venta sin conexión deshecha completa (`SaleTransactionSqlServerTests`, Fase 4G). Desde la Fase 8, `RefreshTokenSqlServerTests` prueba que dos renovaciones con el mismo token reemplazan una sola vez (la perdedora no guarda nada). Desde la Fase 9 también prueba la migración `StopUsingPlainRefreshTokenValues` sobre una base migrada hasta la Fase 8 (`SqlServerTestDatabase.CreateEmpty` y `IMigrator`): el hash que calcula en SQL es el de .NET, los reemplazados quedan rotados, las filas sin hash no abren nada, lo nuevo deja `Token` en NULL y un rollback seguido de aplicar otra vez da lo mismo; desde la Fase 10, la misma prueba aplica después `DropPlainRefreshTokenColumns` con las sesiones funcionando, revierte a la Fase 8 (columnas otra vez, vacías) y vuelve a aplicar. `PurchaseReceptionSqlServerTests` (Fase 11) prueba que una recepción enviada dos veces con su llave se registra una vez, y que cuando las dos copias llegan a la vez la tardía se deshace completa (sin existencias, cambio en la orden ni folio). `UnmappedColumnsSqlServerTests` (Fases 12 y 13) prueba los dos pasos con que se fueron cuatro columnas: migrada hasta `StopMappingStockAndLoyaltyColumns`, las filas que guarda esta versión las dejan en NULL y en 0 y lo capturado sobrevive a una edición; `DropStockAndLoyaltyColumns` las borra sin afectar las filas; revertirla las devuelve vacías y aplicarla otra vez las borra de nuevo. `UserRoleStorageSqlServerTests` (Fase 9) prueba que `SuperAdmin` se guarda como 0 y que `None` no se guarda; `OrderTenantBackfillSqlServerTests`, que las partidas y cambios de estado que la versión anterior agrega sin negocio quedan ocultos hasta recibir el de su orden. El mismo job corre las `*SqlServerTests` de `WebApi.Tests` (la suite HTTP). El job las elige por el nombre de la clase, así que en los dos proyectos `SqlServerFactPlacementTests` falla si una prueba `[SqlServerFact]` queda en una clase con otro nombre, que se omitiría en todos los jobs. `TransactionBehaviorTests` (SQLite) prueba commit, rollback, reintento y unión a una transacción abierta.
- Pruebas contra Azurite (Fase 8): las clases `*AzuriteTests` de `Infrastructure.Tests` corren en el job `Backend` contra el emulador de Blob Storage (`TIENDITAPP_TEST_AZURE_BLOB`) y se omiten en el resto: el contenedor privado se crea sin acceso anónimo y uno que lo permita se rechaza, un archivo guardado no se sobrescribe y los archivos que eran públicos se leen hasta moverse al contenedor privado, sin tocar las imágenes del catálogo. Desde la Fase 9, la subida rechaza carpetas y nombres que saldrían de su carpeta.
- Fase 15: `TabItemCancellationTests` (Domain) fija la regla de cancelar un platillo (importe con el redondeo del renglón, solo lo no pagado, motivo obligatorio, nada cambia al rechazarse); `TabItemCancellationFlowTests` (Application, con el servicio de inventario real) cubre el regreso de insumos con sus dos topes y sin folio de más, la merma, el reverso de la cuenta, el cierre de una cuenta antigua, la cocina, la factura, la ficha del cliente y la bitácora; `TabRoundStockSqlServerTests` y la suite entre negocios lo prueban por la API contra SQL Server, y `UnmappedColumnsSqlServerTests` la columna que deja de mapearse. En el cliente, `SellPage.test.tsx` se escribió antes de dividir Vender y pasó igual antes y después; `TabPanel` y `CancelTabItemForm` tienen sus pruebas.
- Fase 16, temas por negocio:
  - **Backend:** `BusinessThemeTests` (Domain) cubre el tema por omisión, los valores guardados y el rechazo de uno que no existe. `BusinessThemeFlowTests` (Application) cubre el cambio con su registro en la bitácora, que no se repite con el mismo tema, que solo toca al negocio en alcance y que el perfil trae el tema; `OnlineMenuTests`, que el menú en línea lo trae.
  - **SQL Server:** `BusinessThemeSqlServerTests` prueba la columna nueva (sin nulos, 0 por omisión), el rollback que solo quita la elección y la vuelta a Nopal; la suite entre negocios, que con el encabezado de otro negocio un dueño solo cambia su propio tema.
  - **Cliente:**
    - `businessThemes.test.ts` mide con `utils/contrast.ts` cada par de colores de cada tema en claro y en oscuro. Lee `theme.css` como texto (`?raw`, que `css.include` deja pasar en `vitest.config.ts`) para revisar los colores de estado y los chips en los dos modos, que el bloque oscuro defina todas las variables del claro y que los valores por omisión sean Nopal.
    - `theme.test.ts`, `ThemeCard.test.tsx` y `PublicMenuPage.test.tsx` cubren aplicar, previsualizar, guardar y descartar el tema, y el menú con los colores de su negocio.
    - De 11 mutaciones del cliente, las pruebas detectan las 11.
- Fase 17, pantallas de trabajo (cliente):
  - `navigation.test.ts`: dónde sale completo el aviso del plan.
  - `attention.test.ts` y `AttentionBell.test.tsx`: el pago vencido en la campana, primero y contado.
  - `KitchenBoard.test.tsx`: una sola acción por comanda y el paso que da.
  - `PageTitle.test.tsx`: migas solo en los detalles.
  - `Dashboard.test.tsx`: sin un segundo "Nueva venta", en singular cuando es uno, sin "Pedidos por estado" y con la columna Tipo solo cuando hace falta.
  - `industries.test.ts`: los giros guardados con otras mayúsculas o sin acentos.
  - Las de Cocina, migas e Inicio se escribieron antes del cambio y fallaron con el código anterior.
- Fase 18, listas e Inventario:
  - **Backend:** `OrderSearchTests` (Application) filtra la lista de pedidos por canal (mostrador, WhatsApp y menú en línea) y rechaza un canal que no existe.
  - **Cliente:**
    - `DataTable.test.tsx`: búsqueda, filtros y acción en una sola barra; abajo, el tamaño de página solo cuando hace falta y los botones de página con nombre; la búsqueda local vuelve a la primera página.
    - `OrdersList.test.tsx`: el filtro Canal y "Limpiar filtros"; la etiqueta del canal en lugar de "Creada por…"; "Tipo" solo cuando el negocio vende y da servicios, y los estados del tipo elegido.
    - `navigation.test.ts`: Compras como sección e Inventario sin segunda fila de pestañas.
    - `KitchenBoard.test.tsx` y `TabPanel.test.tsx`: las rondas dicen "En preparación".
    - `inventoryForms.test.ts` y `ProductStockCard.test.tsx`: los textos nuevos de existencias.
  - Las de Pedidos y de las rondas se escribieron antes del cambio y fallaron con el código anterior.
- Fase 19, menú en línea que vende:
  - **Domain:** `OpeningHoursTests` cubre el horario del día, el que termina al día siguiente, las 24 horas y los horarios que se juntan entre días. También cubre cuándo cierra o abre y el rechazo de días encimados, de tres horarios en un día y de un horario que termina después de que abre el día siguiente.
  - **Application:**
    - `OpeningHoursFlowTests`, el horario: guardarlo y quitarlo con su registro en la bitácora, sin repetirlo cuando no cambia; rechazar uno encimado sin tocar el guardado; cambiar solo el del negocio en alcance; y el perfil que lo trae.
    - `OpeningHoursFlowTests`, el menú: cerrado dice cuándo abre y rechaza el pedido para recoger aunque haya una caja abierta; abierto dice cuándo cierra; desde una mesa del negocio recibe el pedido, y no con una mesa que el negocio no tiene, que está fuera de uso o que es de otro negocio; sin horario nunca se ve cerrado; y trae la dirección en una línea.
    - `GetMyTenantsQueryTests`: los negocios de la cuenta por nombre, con su logo chico.
  - **SQL Server:**
    - `OpeningHoursSqlServerTests`: la columna con nulos, el JSON que guarda, la vuelta y el rollback que solo quita los horarios.
    - La suite entre negocios: con el encabezado de otro negocio, un dueño solo cambia su propio horario.
  - **Cliente:**
    - `openingHours.test.ts`: el horario en palabras, la semana desde el lunes y el estado abierto o cerrado.
    - `OpeningHoursCard.test.tsx`: un día que se enciende y el que copia su horario, el guardado, el segundo horario y el que termina al día siguiente, las horas que faltan, el error de la API y "Quitar horario".
    - `PublicMenuPage.test.tsx`: abierto y cerrado, la semana, "Cómo llegar", el pedido desde una mesa, las iniciales y las categorías que se desvanecen.
    - `PhotoOrInitial`, `CatalogPanel`, `BusinessSwitcher`, `SaleTicket` y `scrollFade`: la inicial donde falta una foto, el logo en el selector y en el ticket, y los bordes que se desvanecen.
  - Las del cliente se escribieron antes del cambio; las de la tarjeta de horario, el menú, Vender, el ticket y los bordes fallaron con el código anterior.
- Llave del cobro en Vender (2026-10-10, después de la 20A):
  - **Application:** `OfflineSalesTests` cubre la misma llave con otro efectivo recibido, que devuelve la venta registrada con su único pago, y la misma llave en otro negocio, que es otra venta y no responde con la del primero.
  - **Cliente:** `SellPage.test.tsx` desmonta la pantalla a mitad de un cobro sin respuesta y la monta otra vez, como una recarga: el reintento manda la misma llave, un carrito cambiado manda otra, un rechazo se reintenta con la suya y la venta siguiente a una registrada lleva llave propia. `sellDraft.test.ts` cubre la llave guardada con el borrador del negocio y la huella de la venta.
  - El reintento tras la recarga y el carrito cambiado tras un rechazo fallaron con el código anterior.
- Fase 20D, clases propias (cliente): `hyperClasses.test.ts` junta las palabras de cada texto del código con el compilador de TypeScript y falla si una es una clase que solo existe en Hyper, sin regla en Bootstrap, en Remix ni en nuestro CSS; su lista `NOT_CLASSES` (palabras que no se usan como clase) también falla si guarda una que el código ya no usa. Su primera prueba revisa que las hojas lleguen con contenido: sin `?raw` en `css.include` llegaban vacías y las otras dos pasaban sin revisar nada. Quitar la regla de `table-centered` o dejar una palabra de más en la lista las hace fallar.
- Fase 20E, Bootstrap desde Sass (cliente): `hyperClasses.test.ts` toma los nombres de clase de Hyper de `hyperClassNames.json` (la hoja ya no está) y su primera prueba revisa que la lista llegue completa. Antes de las escenas, una comparación regla por regla de la hoja compilada contra la de Hyper, y del CSS global contra el de `master` (los dos minificados igual, prefijos de navegador aparte), mostró que lo único distinto era lo que la app no dibuja, formato que significa lo mismo y el `flex-shrink: 0` de los spinners de Bootstrap 5.3.8. Las escenas encontraron una diferencia más: el muestrario dibuja pestañas como enlaces y había perdido `.nav-tabs > li > a` (16 revisiones de estilos); volvió a `_additions.scss` y las escenas dieron cero diferencias. La comparación de reglas no basta sola: decide si una regla «se usa» por las palabras del código, y el muestrario dibuja también lo que la app podría usar. Tampoco ven los prefijos de navegador las escenas, que corren en Chromium, ni la comparación, que los ignora: la revisión después del merge encontró que faltaba `-webkit-appearance` (Safari e iOS antes de 15.4) y una comparación aparte de los prefijos lo confirmó; `build.cssTarget` los devuelve.
- Fase 20F, arreglos visibles (cliente): `focusRing.test.ts` revisa que el anillo de foco sea una sola regla de `theme.css` y que ninguna hoja nuestra, Sass incluido, lo quite con `outline: 0` o `none`. `businessThemes.test.ts` revisa que el anillo pase 3:1 en cada superficie donde puede quedar con cada tema; que el texto general, el secundario y los de estado del modo oscuro pasen AA en cada superficie oscura y en el tinte de cada tema; y que el nombre del menú condensado use el texto hecho para el acento (`shell.css`). Escenas nuevas: `foco.spec.ts` (el anillo en el muestrario y en el marco, en claro y en oscuro, con el estilo calculado de cada pieza tras un clic y con el teclado), el menú condensado con el puntero en modo oscuro y en una ventana más baja que el menú, y la búsqueda de Productos con texto en los dos modos. Los arreglos se aplicaron uno por uno, con una corrida completa en cada paso, y cada escena que cambió contra `master` la explica uno de ellos; `focusRing.test.ts` también falla si una regla mezcla `:focus-visible` con otros selectores, porque Safari antes de 15.4 la descarta entera.
- Fase 21, días especiales y horario en WhatsApp ([ADR 0012](../adr/0012-special-days.md)):
  - **Domain:** las 16 de `OpeningHoursTests` siguen sin cambios: son la red del evaluador compartido. `SpecialDayRuleTests` cubre las reglas de un día (cerrado sin horario, uno o dos horarios, las 24 horas solas, el motivo de 40 caracteres en una línea) y la igualdad por valor. `SpecialDayCalendarTests` cubre los límites con sus mensajes, el encimado que nombra la fecha, el tope de 120 fechas, el periodo en curso que se divide, termina ayer o no mueve su primer día, y las peticiones que no cambian nada. `BusinessScheduleTests` cubre la precedencia, la noche que pasa a un día cerrado, la que quita un día cerrado (su motivo pasada la medianoche), las vacaciones largas, el negocio que abre solo los sábados, los motivos (`ClosedFor`), el negocio sin horario de la semana (su horario especial cierra cuando dice, aunque el día siguiente abra todo el día) y el cambio de horario de Tijuana y Matamoros. `SpanishCalendarTests`, las fechas en palabras.
  - **Application:**
    - `SpecialDaysFlowTests`: agregar, cambiar y quitar con una entrada en la bitácora cada uno, sin nada en un rechazo o una petición repetida; la versión vieja (`Stale`); el hoy del negocio en su zona; solo el negocio en alcance; la lista con sus límites y sugerencias; los validadores; y las dos carreras (el mismo periodo guardado a la vez y la fecha tomada) sobre SQLite.
    - `HolidaySuggestionsTests`: los festivos que el negocio abriría, el cierre temprano que conserva el descanso, sin horario solo cerrados, el tope de 8, la Pascua, Jueves y Viernes Santo y los lunes que se mueven de 2026 a 2030, y el 1 de octubre solo en su año.
    - `BusinessHoursTextTests`: la tabla de cada texto que escribe el servidor (las fechas en palabras, el chip y su detalle, los avisos, el pedido rechazado como el de la Fase 19 a 0 y 1 días salvo una apertura a medianoche ("abre mañana"), el motivo del día especial que fija el cierre aunque sea el de ayer o el de mañana, la mesa sentada, sin horario de la semana, "a la 1:30" en singular, el horario especial en curso y las respuestas de WhatsApp).
    - `SpecialDaysMenuTests`: el menú y el pedido en Navidad y el 24 en la noche, la noche anterior a un día cerrado que recibe pedidos hasta su cierre, la mesa rechazada en un día cerrado y en su noche pasada la medianoche (`PublicOrderRules.TableOverridesClosure`), un horario especial en curso en la lista y en el aviso, los próximos 31 días, sin horario de la semana, sin horario y dos negocios en la misma fecha. `OnlineMenuTests` y `OpeningHoursFlowTests` siguen sin cambios.
    - `AutoReplyHoursTests`: los textos exactos de WhatsApp, las preguntas que no son de horario ("¿A qué hora llega mi pedido?"), el orden con el saludo y la descripción, el catálogo mientras está cerrado, los límites de tamaño (bienvenida y productos largos, el peor caso en 1,300 caracteres sin cortar), el reloj que falla con un solo error en el log, dos negocios, la noche del cambio de horario en Tijuana, el horario especial en curso y el de hoy completo, sin horario de la semana un horario especial hasta o después de la medianoche, y un corte que no parte un emoji. `AutoRepliesAndChatTests` sigue sin cambios.
    - `TimeZoneDataTests`: cada zona que un negocio puede elegir existe en el servidor de CI, con su diferencia en verano y en invierno. Solo cuida ese servidor; en el App Service lo cuida `business-time-zones`.
    - `UseCaseRulesTests` suma las cuatro rutas como de dueño o administrador, y `PerBusinessLimitsTests` el tope de 120 fechas, que no cuenta las de otro negocio.
  - **SQL Server:** `SpecialDaysSqlServerTests` (la tabla, sus restricciones, la carrera y el rollback, ver [migraciones](database-migrations.md)); `PublicMenuSpecialDaysSqlServerTests`, el menú y el pedido por HTTP en un día cerrado, también desde una mesa, sin tocar el menú del otro negocio; y en la suite entre negocios, días especiales que con el encabezado de otro negocio solo leen y cambian los propios.
  - **WebApi:** `BusinessTimeZonesHealthCheckTests` (Degraded cuando una zona cae en UTC, con un resolvedor inyectado), `HealthEndpointsTests` (`/health/ready` trae `"business-time-zones":"Healthy"` tal como lo busca la prueba de humo) y `SpanishModelBindingTests` (una fecha mal escrita en el cuerpo responde 400 en español).
  - **Cliente:** `SpecialDaysCard.test.tsx` (estados, sin próximos con Pasados, agregar, dos horarios, varios días, editar uno en curso, quitar, el aviso al guardar según lo que muestra el menú, los errores 400, 404, 409 y 422 que vuelven a pedir la lista y cierran el diálogo que ya no puede guardarse, uno de red que no la relee, la noche anterior con un día especial marcado, una sugerencia que llena el diálogo sin pedir nada y los nombres accesibles), `PublicMenuPage.test.tsx` (las palabras del servidor, la mesa en un día cerrado, sin horario de la semana, la línea de hoy sin nada que abrir y la respuesta anterior sin `todayDate`, que se ve igual que antes), `OpeningHoursCard.test.tsx` (los textos con días especiales y el aviso al guardar), `AiAgentSettings.test.tsx` (de dónde salen las respuestas de horario, con y sin horario de la semana), `dates.test.ts` (el 24 de diciembre sigue siendo el 24 en la zona de México) y `openingHours.test.ts` (sus casos anteriores, "a la 1:30" en la respuesta anterior, y los textos nuevos).
  - **Escenas visuales:** cuatro nuevas, "negocio, días especiales" y "negocio, día especial (diálogo)" en `caja-config.spec.ts`, y el menú cerrado por un día especial y con horario especial en `publico-cuenta-marco.spec.ts`. Los datos simulados compartidos responden la lista de días especiales, así que las escenas de Mi negocio cambian por la tarjeta nueva; los datos de las del menú público no traen `todayDate`, así que siguen el camino de antes.
- Fase 22F, pedidos de Rappi, Uber Eats y DiDi Food ([ADR 0013](../adr/0013-delivery-platform-orders.md)):
  - **Domain:**
    - `PlatformTermsTests`: los rangos, los valores por omisión de 2026 y la igualdad sin importar la escala.
    - `PlatformOrderNumberTests`: la normalización del folio, los 30 caracteres, los 60 antes de normalizar y la búsqueda.
    - `PlatformOrderCalculatorTests`: los ejemplos 561.48, 574.41, −348.00, 191.25 y 406.14, el punto medio y los exentos.
    - `OrderChannelRulesTests`: recorre todo el enum.
    - `DeliveryPlatformSettingTests`: la revisión, sin cambios y el canal fijo.
    - `PlatformOrderTests`: el constructor, `RegisterPlatformOrder` con las dos opciones de quién cobró, cada guarda y el cierre con y sin cocina.
    - Ampliaciones: `OrderNotificationRulesTests` (los canales 3 a 5 nunca avisan, con las tres políticas), `CashSessionTests` (una transacción «Plataforma» se rechaza), `FractionalQuantityAndCfdiTests` (forma 31), `BranchTests` (`PlatformAmount`) y `OrderTaxTests` (`RecalculateTotals` ya no recorta lo cobrado).
  - **Application:**
    - `PlatformOrderFlowTests`, el registro: cobrado por la plataforma sin caja ni `ChargeOrders`, cobrado por el negocio al crear y después, plataforma inactiva, el validador, el producto fuera de horario o apagado, el servicio, la política `Block`, el folio repetido el mismo día del negocio (no al siguiente, ni con el anterior cancelado o reembolsado por completo) y el reintento con su folio.
    - `PlatformOrderFlowTests`, lo demás: el detalle y Cocina, el evento y la bitácora, sin avisos con `AllOrders`, las campañas y la ficha.
    - `OrderRefundTests`: la anulación sin caja, la entregada que queda reembolsada, los insumos solo si se piden y las reglas de método.
    - `CashShiftTests`: los cinco casos del corte, con I5 como teoría sobre todo el enum. `PaymentNoDrawerTakes_ShouldBeRefused_WhateverTheMethod` sigue igual.
    - `DailySalesReadModelTests`: `PlatformAmount` e I6.
    - `GetSalesByChannelQueryTests`: I1, I2, I3, I7 e I8, la anulación otro día, el reembolso parcial, el negocio que cobra, las activas en cero, la sucursal, los módulos, el rango y el validador.
    - `GetDeliveryPlatformOrdersQueryTests` y `GetDashboardMetricsQueryTests` (`PlatformCollected`).
    - `DeliveryPlatformSettingsFlowTests`: los valores por omisión, crear, actualizar, sin cambios sin bitácora, `Stale`, 16/8/0, términos guardados fuera de los límites, dos negocios y `/channels` sin tasas.
    - Otras: `PlatformPaymentValidatorTests`, `DtoMappingTests`, `OrderSearchTests` ("#bc 953" encuentra `BC953`), `FractionalSaleAndCfdiDraftTests` (forma 31, PUE y avisos, I9) y las cinco líneas nuevas de `UseCaseRulesTests`.
  - **Infrastructure (sin SQL Server):** `SalesSnapshotWorkerTests` (el worker guarda `PlatformAmount` aparte de «Otros», sobre EF InMemory) y `OutboxTests` (un `OrderCreatedEvent` guardado antes, sin canal, folio ni quién cobró, se lee como de mostrador).
  - **SQL Server:**
    - `DeliveryPlatformsSqlServerTests`: la forma, las restricciones, la precisión, el índice único y las dos carreras del guardado.
    - `DeliveryPlatformsMigrationSqlServerTests`: el error 50008, el rollback limpio y el contexto de la versión anterior (ver [migraciones](database-migrations.md)).
    - `SaleTransactionSqlServerTests`: una sola transacción; un rechazo sin detalle y con el folio devuelto; el folio repetido con otra llave que avisa y nunca da 500.
    - `SalesByChannelSqlServerTests`: la traducción del `GROUP BY`, las sumas condicionales con división decimal y `COUNT DISTINCT`, e I1 a I4, I7 e I8 contra las ventas por sucursal, Inicio y los cortes, con días guardados.
    - Otras: `DtoProjectionSqlServerTests`, `DashboardMetricsSqlServerTests` y `CampaignAudienceSqlServerTests`.
  - **WebApi:**
    - `PlatformOrdersSqlServerTests`: 409 `possible_duplicate_order` y 201 con la confirmación; 400 `business_rule` con la plataforma inactiva.
    - `CrossTenantDeliveryPlatformsSqlServerTests`: dos negocios, la revisión vieja con 409, el personal con 403 y `/channels` para cocina y reportes, el pedido de una plataforma que solo activó el otro negocio, y las listas, búsquedas y reportes sin los pedidos del otro.
    - `CrossTenantManagementSqlServerTests` suma las ventas por canal.
  - **Cliente:**
    - Utilidades: `labels.test.ts`, `deliveryPlatforms.test.ts`, `ticket.test.ts`, `cashCutText.test.ts` y `deliveryPlatformTerms.test.ts`.
    - Componentes y pantallas: `ChannelBadge`, `PlatformOrderFields`, `sellDraft`, `SellPage` (las plataformas activas, el folio, la petición sin pagos ni caja, «Cobrar ahora» sin propina, sin conexión, el 409 con la misma llave, el catálogo sin horario y el regreso a Mostrador), `CompletedSaleModal`, `SaleTicket`, `OrdersList`, `RefundOrderModal` y `KitchenBoard`.
    - Configuración, reportes, corte e Inicio: `DeliveryPlatformsCard`, `SalesByChannelCard` (con el diálogo y la semana pasada), `SalesByBranchCard`, `DailyCut`, `CashCutTicket` (sin filas de plataforma en el corte de un turno) y `Dashboard`.
  - **Escenas visuales:**
    - **Nuevas:** diez escenas con dieciséis capturas: Vender un pedido de Rappi en escritorio y teléfono, el pedido registrado, Pedidos por canal, el detalle, Cocina, Plataformas de reparto con el formulario, Ventas por canal con sus pedidos y el corte del día con plataforma.
    - **Cambian a propósito:** doce escenas de Mi negocio y Reportes, por las tarjetas nuevas.
    - **Resultado:** las 335 capturas pasaron dos corridas seguidas. Las nuevas no salen de la línea base de `master` hasta que la fase entre a `master`.
- Fase 22F-2, precios en las apps de las plataformas ([ADR 0014](../adr/0014-delivery-platform-prices.md)):
  - **Domain:** `ProductPlatformPriceTests` (27 casos): `PriceIn` y `PriceDeltaIn` en cada canal (solo las plataformas usan la lista), los límites del producto y de la opción con sus mensajes, null que vuelve al menú, un canal que no es plataforma, una opción de otro producto, `ResolveOptions` y `PriceWith` con los precios de la app (sin bajar de 0) y el editor de opciones que conserva los precios de las que se quedan.
  - **Application:** `PlatformPricesFlowTests`:
    - el pedido de Rappi cobrado con los precios de la app, con su comisión, y rechazado con el total del menú;
    - los demás canales con el precio del menú y cada plataforma con el suyo;
    - el guardado que pone y quita precios sin tocar lo que no se manda ni las otras plataformas, todo o nada;
    - un servicio o un producto de otro negocio rechazados, y el validador, también con un elemento null;
    - el guardado rechazado en un negocio sin el módulo de productos ni el de alimentos, cuya lista sale vacía;
    - la lista por categoría con los dos precios, en su negocio;
    - el catálogo de Vender con los precios de la plataforma y sin el tachado donde aplican.

    `UseCaseRulesTests` suma la lista y el guardado como `Permission(ManageCatalog)` y `/channels` con `ManageCatalog`.
  - **SQL Server:** `ProductPlatformPricesSqlServerTests` (la forma, las restricciones, los centavos, la cascada, el negocio y las carreras del guardado: el primer precio guardado por otra persona o el producto borrado mientras tanto responden «Alguien más modificó…», y una falla que no es un conflicto sigue como error) y `PlatformPricesMigrationSqlServerTests` (la versión anterior sobre el esquema nuevo, el error 50009 y el rollback limpio, ver [migraciones](database-migrations.md)). `DeliveryPlatformsMigrationSqlServerTests` aplica ahora hasta su propia migración, no hasta la última.
  - **WebApi:** `CrossTenantPlatformPricesSqlServerTests`: dos negocios que solo ven y cambian sus precios, también con el encabezado del otro; el pedido de Rappi cobrado de punta a punta con los precios que Vender lee; el permiso (catálogo sí, cajero y reportes no) y los rechazos (`[JsonRequired]`, un elemento null, un canal que no es plataforma, el tercer decimal). `CrossTenantDeliveryPlatformsSqlServerTests`: el personal de solo catálogo también recibe 200 en `/channels`.
  - **Cliente:**
    - `platformPriceEdits.test.ts`: lo que acepta cada caja (como la API, y la coma solo entre miles), la caja que solo muestra el precio guardado, el guardado de solo lo que cambió, el rechazo con una caja inválida, el cálculo en centavos enteros y el porcentaje.
    - `PlatformPrices.test.tsx`: la lista de la primera plataforma activa con las opciones bajo su producto, la plataforma apagada, guardar solo lo cambiado, el guardado bloqueado con una caja en rojo, el cálculo que no guarda nada, cambiar de plataforma con cambios, el rechazo de la API que conserva lo escrito, descartar y lo escrito que se queda en su plataforma cuando otra se activa mientras tanto.
    - `SellPage.test.tsx`: el catálogo con los precios de Rappi al elegirla y de vuelta en Mostrador; las acciones bloqueadas mientras cargan; el diálogo de opciones que toma los precios que llegan con él abierto; el ticket que dice que no cargaron, con «Reintentar»; y la búsqueda de un catálogo grande con los precios de la plataforma.
    - `DeliveryPlatformsCard.test.tsx`: el aviso de precios y su enlace a Precios en apps para quien maneja el catálogo.
  - **Escenas visuales:**
    - **Nuevas:** cinco escenas con cinco capturas: Precios en apps en escritorio, teléfono y oscuro, el diálogo «Calcular precios» y la lista con cambios y una caja en rojo.
    - **Cambian a propósito:** veintidós capturas. Diecisiete por la pestaña nueva de Catálogo: Productos, Servicios, Categorías y sus diálogos en escritorio y oscuro, y la impresión del producto nuevo. Las cuatro de Vender un pedido de Rappi, que cobra los precios de la app ($220.00 y «Con tus precios de Rappi»). Y la última del diálogo de Plataformas de reparto, cuyo aviso enlaza a Precios en apps.
    - **Resultado:** las 340 escenas pasaron dos corridas seguidas. Las nuevas, y las de Rappi con los precios de la app, no salen de la línea base de `master` hasta que la fase entre a `master`.
- Fase 22G, liquidación de los depósitos de las plataformas ([ADR 0015](../adr/0015-delivery-platform-settlements.md)):
  - **Domain:** `PlatformSettlementTests` (26 pruebas, 34 casos):
    - el neto esperado por lo que sigue cobrado, con redondeo al par;
    - los límites del periodo (también que no empiece antes de 2000), de la fecha del depósito, de lo depositado, de la referencia y de las notas;
    - los pedidos de su plataforma hasta el final del periodo, una vez cada uno; uno que no trae nada no entra, y uno ya incluido que se devolvió se queda en 0;
    - los ajustes con motivo e importe («Otro» con descripción), numerados en el orden en que se escribieron;
    - guardar entera conservando las filas de los pedidos que se quedan, y un guardado rechazado que no cambia nada;
    - contar otra vez con revisión nueva solo si algo cambió;
    - cerrar con cada peso explicado y algo que cerrar; una cerrada que ya no cambia pero se anula; anular con motivo, una sola vez, liberando sus pedidos;
    - los números guardados de cada enum.
  - **Application:** `PlatformSettlementFlowTests` (18 pruebas):
    - los pendientes hasta el periodo con lo que trae cada uno ahora; con 2,050 de antes sin liquidar, los del periodo y los 1,997 más recientes de antes (los más viejos, en una liquidación de su semana); un periodo que termina después de hoy, rechazado;
    - abrir contando cada pedido y dejando los demás como candidatos;
    - una segunda abierta, un pedido de otra plataforma o de otro negocio y uno de una liquidación vigente, rechazados hasta anularla;
    - las reglas del dominio como reglas de negocio, y un pedido devuelto completo o sin cobrar que no entra;
    - guardar con la revisión vista;
    - cerrar con diferencia 0 y su bitácora, y la devolución en medio que guarda las cifras nuevas;
    - el detalle que avisa un pedido que cambió;
    - anular con motivo y su bitácora;
    - lo que debe cada plataforma y hasta dónde liquidó, con un pedido devuelto a medias contado uno por uno;
    - la lista por plataforma y estado y la búsqueda por referencia o notas;
    - el detalle de otro negocio, que no existe.

    `UseCaseRulesTests` suma las cuatro lecturas como `Permission(ViewReports)` y los cuatro cambios como `Manager`.
  - **SQL Server:** `PlatformSettlementsSqlServerTests` (12 pruebas: los nueve índices con los dos únicos filtrados y las tres llaves; el guardado sin perder centavos y en su negocio; los pedidos que salen; los ajustes que vuelven en el orden en que se escribieron después de cada guardado; una abierta por plataforma; un pedido en una sola vigente; la revisión vieja; la llave sin cascada del pedido; las restricciones; y las carreras de los casos de uso con `SavedMeanwhile`: otra abierta o un pedido tomado mientras se abre una, y el mismo guardado enviado dos veces, que responden «Alguien más modificó…» en lugar de un error) y `PlatformSettlementsMigrationSqlServerTests` (la versión anterior que reembolsa un pedido liquidado, el error 50010 y el rollback limpio, ver [migraciones](database-migrations.md)). `PlatformPricesMigrationSqlServerTests` aplica ahora hasta su propia migración, no hasta la última.
  - **WebApi:** `CrossTenantPlatformSettlementsSqlServerTests`:
    - dos negocios que solo ven y cambian sus liquidaciones, también con el encabezado del otro;
    - el ciclo completo en SQL Server: abrir, la segunda abierta rechazada, no cerrar con diferencia, la revisión vieja (409 `concurrency_conflict`), cerrar con su bitácora, el pedido tomado por otra (409 `conflict`) y anular;
    - el permiso: reportes lee, y ni reportes ni el cajero cambian;
    - los rechazos: un canal que no es plataforma, `[JsonRequired]`, un ajuste null, el tercer decimal y el periodo que falta.
  - **Cliente:**
    - `settlementEdits.test.ts`: los importes con signo como la API (la coma solo entre miles), el periodo (también un año a medio escribir) y la fecha del depósito, dónde cae cada pedido, cada ajuste, los propuestos y los guardados, las cuentas en centavos, el cuerpo sin textos vacíos, qué cambió desde el guardado (una caja de depósito que la API rechazaría siempre cuenta, también sobre $0) y el ajuste por la diferencia.
    - `reportPeriods.test.ts`: la semana pasada y el texto del periodo.
    - `PlatformSettlementPage.test.tsx`:
      - una nueva con los pedidos de la semana, la diferencia explicada con un ajuste y el guardado;
      - lo que la API rechazaría, marcado sin guardar;
      - la segunda abierta de una plataforma que no se abre;
      - otro periodo, que no deja guardar hasta tener sus pedidos y guarda solo los suyos;
      - el pedido que otra liquidación tomó al guardar: se vuelven a leer los pedidos y se queda lo escrito;
      - la liquidación abierta que aparece al guardar una nueva, con su enlace;
      - descartar y cancelar preguntando, y el aviso al cerrar la pestaña;
      - cerrar después de confirmar, el 409 al cerrar que vuelve a leer las cifras y el de otra persona que muestra su versión;
      - el pedido que cambió y pide guardar;
      - la versión de otra persona al guardar;
      - anular con motivo;
      - la vista de solo lectura, la nueva sin permiso y la liquidación que no existe.
    - `PlatformSettlements.test.tsx`: lo que debe cada plataforma con sus botones según el rol, la invitación a activar una plataforma, la lista con filtros y búsqueda en el servidor, el estado en el nombre de cada enlace (una anulada y la que la reemplazó) y un error que no borra la búsqueda ni los filtros.
    - `DataTable.test.tsx`: con menos filas, la última página que todavía tiene alguna, y el largo máximo de la búsqueda.
    - `navigation.test.ts`: las pestañas «Ventas» y «Liquidaciones» de Reportes y la búsqueda global.
  - **Escenas visuales** (`liquidaciones.spec.ts`, contra `master`): 9 nuevas, la lista y una liquidación abierta en escritorio, teléfono y oscuro, una nueva, una cerrada y el diálogo de anularla. Cambian a propósito las 3 escenas de Reportes (las pestañas «Ventas» y «Liquidaciones») y la tarjeta de Ventas por canal, que se recorre debajo de ellas. Las 349 pruebas pasan dos corridas seguidas contra el mismo build.
- Fase 22A, sucursal solo a domicilio ([ADR 0016](../adr/0016-delivery-only-branches.md)):
  - **Domain:** `BranchTests`: una sucursal tiene local salvo que solo entregue, y entonces guarda su dirección para el equipo; la principal de un negocio sin local; y un modo que no existe, rechazado.
  - **Application:**
    - `OpeningHoursFlowTests`: el menú de una principal solo a domicilio no lleva su dirección y dice que es solo a domicilio, y otra sucursal solo a domicilio no cambia el menú de una principal con local.
    - `AutoReplyHoursTests`: «¿Dónde están?» con la principal solo a domicilio responde que no hay local, la descripción y el menú, nunca la calle; con el horario, con un saludo (bienvenida antes, nombre al final) y con una pregunta de precios (productos después) también; «mi dirección» y «mi ubicación» no lo activan; con la principal con local sigue respondiendo con la descripción; y de dos negocios, solo el que no tiene local lo dice.
    - `BranchesTests`: crear una sucursal solo a domicilio; una sin el campo tiene local; guardar sin el campo conserva cómo atiende, y guardar con local la regresa; un modo desconocido, rechazado por los validadores.
    - `CreateBusinessCommandTests`: el alta (`RegisterBusinessCommand`) con el giro «Comida a domicilio» (con otras mayúsculas y espacios alrededor también) empieza con la principal solo a domicilio, y con otro giro o sin giro, con local.
    - `TenantSetupTests`: la plantilla «Comida a domicilio» con sus cinco categorías y diez productos con el IVA del negocio.
  - **SQL Server:** `BranchServiceModeMigrationSqlServerTests` (la versión anterior agrega y edita sucursales; la restricción CHECK; revertir detenido con el error 50011 y después solo lo que agregó).
  - **WebApi:** `PublicMenuDeliveryOnlySqlServerTests`: la respuesta del menú público de una principal solo a domicilio no trae la calle ni el código postal, un guardado sin el campo la mantiene privada y, de vuelta a local, la dirección se publica.
  - **Cliente:**
    - `BranchesList.test.tsx`: el grupo «¿Cómo atiende?» con su explicación, el aviso de la dirección privada, el guardado con `serviceMode` cuando cambia y sin él cuando no, la sucursal nueva que empieza como la principal (sin aviso si no es la principal), lo que dirá el menú al hacer principal una sucursal y la marca en la lista.
    - `PublicMenuPage.test.tsx`: «Solo a domicilio» sin dirección ni «Cómo llegar» (aunque la respuesta trajera una), y el pedido en línea igual.
    - `industries.test.ts`: el giro «Comida a domicilio», escrito como lo lee el servidor.
  - **Escenas visuales** (contra `master` en `a60b1d4`, con la 22G): tres nuevas, la principal solo a domicilio en el formulario de Sucursales y en su lista, y el menú en línea solo a domicilio en el teléfono. Cambian a propósito los 4 diálogos de Sucursales (el grupo «¿Cómo atiende?») y las 9 escenas de Mi negocio (la frase de la dirección fiscal); las demás, sin cambios. Con esas capturas, las 352 escenas pasaron dos corridas seguidas contra el mismo build. Las nuevas no salen de la línea base de `master` hasta que la fase entre a `master`.

- Fase 22B, zonas de entrega ([ADR 0017](../adr/0017-delivery-zones.md)):
  - **Domain:** `DeliveryZoneTests` (27 pruebas):
    - los códigos postales una vez cada uno, en orden y sin espacios;
    - guardar conserva los que siguen, quita los demás y suma los nuevos;
    - guardar sin cambios no sube la revisión;
    - lo que no es un código postal (cuatro o seis dígitos, espacios, letras o dígitos que no son ASCII), rechazado con lo escrito;
    - de 1 a 300 códigos postales;
    - el nombre;
    - los montos fuera de rango o con más de dos decimales;
    - envío gratis con envío $0, rechazado;
    - un guardado rechazado deja la zona como estaba;
    - `Covers` solo mientras está activa;
    - `FeeFor` y `MeetsMinimum`.
  - **Application:**
    - `DeliveryZonesFlowTests`:
      - crear guarda los códigos postales una vez cada uno y deja en la bitácora lo que cobra, sin los códigos postales;
      - el código postal de otra zona, rechazado con su nombre, mientras otro negocio puede tenerlo;
      - el nombre de otra zona, rechazado al crear y al guardar, sin quedarse con el cambio, mientras otro negocio puede tenerlo;
      - el tope de 30 zonas;
      - los mensajes del dominio, sin guardar nada;
      - guardar con la revisión vista, antes y después en la bitácora, y una revisión vieja rechazada;
      - guardar sin cambios no registra nada;
      - tomar el código postal de otra zona solo después de que esa lo suelta;
      - borrar libera sus códigos postales;
      - la zona de otro negocio no se encuentra;
      - la lista por nombre y la búsqueda de una zona activa;
      - los validadores.
    - `AutoReplyDeliveryZonesTests`:
      - un código postal de una zona (en una pregunta, solo, con «C.P.» o con «mi código postal es el») contesta lo que cobra y el menú;
      - el envío gratis;
      - un código postal fuera de las zonas o de una zona desactivada;
      - la pregunta sin código postal, también «¿Llegan a la colonia Roma?», «¿Envían a Cholula?» o «¿Qué zonas cubren?», y el costo del envío sin los productos;
      - «Te envío el comprobante», «Gracias por el envío», «¿Me mandan el número de cuenta?», «¿Me entregas factura?» y «¿En qué colonia están?», como antes: esas palabras cuentan solo junto a un código postal;
      - el costo del envío y los precios del menú en el mismo mensaje;
      - con un saludo, con el horario y con «¿dónde están?» de una cocina sin local, sin la calle;
      - sin zonas activas, como antes;
      - de dos negocios, cada uno con las suyas.
  - **SQL Server:** `DeliveryZonesSqlServerTests` (13 pruebas):
    - índices y llave; guardado en su negocio;
    - el código postal repetido (error 2601), y en otro negocio permitido; lo mismo con el nombre, sin distinguir mayúsculas;
    - guardar conserva las filas y borrar se lleva los códigos; las restricciones CHECK;
    - revertir detenido con el error 50012 y después solo las dos tablas;
    - con los casos de uso y otro guardado justo antes (`SavedMeanwhile`): agregar el código postal que alguien agregó, o guardar una zona que alguien borró, es 409 aunque la base conteste con el índice único (2601) o la llave (547), porque escribe el código postal antes que la zona; el código postal o el nombre que otra zona tomó, con su nombre, al guardar y al crear; borrar una zona que alguien guardó, 409; y un error de otra causa se lanza, no pasa por conflicto. Sin leer otra vez la revisión, las dos primeras respondían 500.
  - **WebApi:** `CrossTenantDeliveryZonesSqlServerTests` (3 pruebas): otro negocio no lee, guarda ni borra las zonas, ni con el encabezado del negocio; el 409 con una revisión vieja y el código postal de otra zona rechazado con su nombre; el equipo las lee y solo dueños y administradores las cambian.
  - **Cliente:**
    - `DeliveryZones.test.tsx`:
      - la lista con lo que cobra cada zona y sus primeros códigos postales;
      - «Probar un código postal»;
      - agregar una zona con sus códigos postales una vez cada uno;
      - los errores antes de guardar y la respuesta del servidor;
      - guardar con la revisión leída; con el 409 o el 404 el diálogo se cierra y la lista se lee otra vez;
      - el nombre de otra zona, sin distinguir mayúsculas, antes de guardar;
      - las acciones de todas las zonas apagadas mientras se guarda un cambio;
      - el aviso cuando ninguna zona está activa;
      - desactivar y eliminar solo con confirmación, con sus revisiones.
    - `postalCodes.test.ts`: cinco dígitos; cada uno una vez, en orden, con cualquier separador; lo que no es código postal, como se escribió.
  - **Escenas visuales** (contra `master` en `3e69f32`, con la 22A):
    - **Nuevas (7):** Zonas de entrega en escritorio, teléfono y oscuro; probar un código postal; editar una zona; nueva zona en el teléfono; y sin zonas. No salen de la línea base de `master` hasta que la fase entre a `master`.
    - **Cambian a propósito (35):** las escenas de Configuración que muestran sus pestañas, ahora con «Zonas de entrega» después de «Sucursales»: Negocio (9), Sucursales (8), Equipo y permisos (6), WhatsApp (3), Respuestas automáticas (5), Seguridad y actividad (3) y Plataformas de reparto (1). Solo cambia la fila de pestañas. En escritorio, «Primeros pasos» queda al final de la fila, que se desplaza, como ya pasaba en pantallas más angostas.
    - **Resultado:** con esas capturas, las 359 escenas pasaron dos corridas seguidas contra el mismo build.

- Fase 22C, pedido a domicilio ([ADR 0018](../adr/0018-home-delivery-orders.md)):
  - **Domain:**
    - `HomeDeliveryTests` (17 pruebas):
      - la dirección sin espacios alrededor, con referencias opcionales, y los textos demasiado largos;
      - el folio de Paquetenvia normalizado y el enlace solo escrito con `https://` (en mayúsculas se guarda en minúsculas); un folio vacío o con otros signos, un enlace `http`, `javascript:`, sin esquema, `https:/`, con barras invertidas o con usuario y contraseña, un repartidor desconocido o un enlace demasiado largo, rechazados;
      - el envío en el total como un concepto más con IVA incluido, con el IVA sumado si los precios no lo incluyen, y con su propia tasa cuando los productos llevan otra;
      - gratis desde el monto de la zona; un pedido por debajo del mínimo, rechazado; una zona del negocio que llegue hoy al código postal;
      - solo una vez, en un pedido nuevo de productos, antes de cualquier pago; nunca un pedido de plataforma;
      - después no toma productos ni se vuelve cuenta; pagado y listo en cocina no se cierra solo;
      - el viaje se anota y se corrige, no en un pedido cancelado ni en uno que no va a domicilio.
    - `OrderNotificationRulesTests`: un pedido tomado en el mostrador para la dirección del cliente le avisa por WhatsApp, como uno del menú.
  - **Application:**
    - `HomeDeliveryFlowTests` (16 pruebas):
      - Vender manda el pedido a la dirección con el envío de la zona y su IVA; gratis desde lo que dice la zona y rechazado debajo de su mínimo; un código postal sin zona activa, rechazado sin guardar nada;
      - pagado al tomarlo sigue abierto hasta que alguien lo marca entregado;
      - el validador rechaza la dirección en un pedido de plataforma, una cuenta, una mesa, un servicio o una venta sin conexión;
      - la bitácora dice que va a domicilio y cuánto cobró, nunca la dirección;
      - con la política de omisión, el cliente de un pedido a domicilio tomado en Vender recibe el aviso por WhatsApp y el de una venta de mostrador no;
      - el borrador de CFDI con el envío como concepto propio que cuadra con el pedido, también con precios sin IVA junto a un producto exento (total $344, IVA trasladado $44, el envío $35 con $5.60 de IVA);
      - el viaje se anota, se corrige y queda en la bitácora sin el enlace; un enlace inseguro, un pedido cerrado o de otro negocio, rechazados;
      - el menú en línea toma el pedido a domicilio con el envío en el total que vio el cliente; una cocina sin local con zonas pide a dónde va, también con una mesa inventada en el enlace («Mesa 9»); una mesa con dirección, rechazada;
      - el menú ofrece domicilio solo mientras toma pedidos y hay una zona activa;
      - la cotización dice lo que cuesta sin el nombre de la zona, y nada sin menú.
    - `AutoReplyDeliveryZonesTests`: con precios sin IVA, el envío se dice «más IVA», como lo cobra el pedido.
    - `AuthorizationBehaviorTests`: la cotización del menú, en la lista de peticiones anónimas.
  - **SQL Server:** `OrderDeliveriesSqlServerTests` (7 pruebas): un envío por pedido que se va con él; el pedido siempre trae su envío, así que un cambio conserva el envío en el total; el viaje se guarda con el pedido y choca (409) con una cancelación guardada al mismo tiempo; la versión anterior sigue tomando pedidos y cobra el total de uno a domicilio; las restricciones CHECK; revertir detenido con el error 50013 y después solo la tabla.
  - **WebApi:** `CrossTenantHomeDeliverySqlServerTests` (2 pruebas): un pedido de otro negocio no usa sus zonas ni anota su viaje; el menú público cotiza y toma el pedido solo con las zonas del negocio del slug.
  - **Cliente:**
    - `homeDelivery.test.ts`: la zona activa que llega al código postal; el envío hasta «gratis desde» y el mínimo contra los productos; el envío como concepto con el IVA del negocio; con productos de otras tasas, igual que el servidor (cada tasa redondeada una vez); los textos del servidor y «más IVA».
    - `PublicMenuPage.test.tsx`: el pedido a domicilio con lo que cobra el negocio a su código postal; dónde no llega o el mínimo, sin mandar nada; con precios sin IVA, el envío con su IVA y renglones que suman el total ($127.60 + $40.60 = $168.20); en una mesa no se ofrece.
    - `SellPage.test.tsx`: el pedido a la dirección con el envío de la zona; dónde no llega o el mínimo, sin mandar nada; sin zonas activas ni en un pedido de plataforma no se ofrece; un domicilio traído con la venta mientras las zonas no se pueden leer se queda y dice por qué, sin mandar la venta.
    - `OrderDeliveryPanel.test.tsx`: la dirección y el envío; anotar y corregir el viaje; un enlace inseguro rechazado antes de mandarlo; un enlace que no empieza con `https://` (también con barras invertidas) nunca se ofrece.
    - `KitchenBoard.test.tsx`, `SaleTicket.test.tsx`, `ticket.test.ts`, `sellDraft.test.ts` y `DeliveryZones.test.tsx`: la cocina marca «A domicilio» sin la dirección; el ticket lleva la dirección, el folio y el envío; el borrador guarda la dirección y distingue otra; Zonas de entrega dice si los montos llevan IVA.
  - **Escenas visuales** (contra `master` en `0eaa1d1`, con la 22B):
    - **Nuevas (10):** Vender a domicilio en escritorio y teléfono, la venta a domicilio cobrada con su ticket, el detalle de un pedido a domicilio con su viaje de Paquetenvia, registrar el viaje, la cocina con un pedido a domicilio, y el menú en línea que también entrega a domicilio, su carrito, por debajo del mínimo y el pedido enviado. No salen de la línea base de `master` hasta que la fase entre a `master`.
    - **Cambian a propósito (7):** las 6 escenas de Zonas de entrega (la explicación dice dónde se usan las zonas y si los montos llevan IVA) y Vender del cajero que entra a Mi negocio sin acceso (sus datos simulados tienen zonas activas, así que el ticket ofrece «A domicilio»).
    - **Resultado:** con esas capturas, las 369 escenas pasaron dos corridas seguidas contra el mismo build.

- Fase 22D, salida a entregar y liquidación del repartidor ([ADR 0019](../adr/0019-courier-cash-on-delivery.md)):
  - **Domain:** `CourierCollectionTests` (36 pruebas):
    - «paga con» se guarda si cubre el total con su envío; debajo del total o fuera de centavos se rechaza y el pedido queda como estaba;
    - la salida guarda el viaje y el saldo que cobra el repartidor; se corrige en camino conservando cuándo salió y sin volver a avisar; cobra solo lo que se debe y necesita algo que cobrar; un billete que no alcanza o «paga con» sin cobrar, rechazados; solo un pedido confirmado a domicilio, ni entregado ni cancelado;
    - anotar el viaje refresca las pantallas pero no avisa al cliente;
    - el repartidor debe el pedido solo entregado y mientras el negocio no lo cobre;
    - la liquidación paga cada pedido y explica el resto con sus ajustes; cuadra al centavo o dice la diferencia; cada error con su mensaje (repartidor, caja, método, montos, pedidos repetidos o de más, ajustes y sus signos, descripción, referencia y notas);
    - los valores del enum de ajustes se quedan como se guardan.
  - **Application:**
    - `CourierSettlementFlowTests` (11 pruebas):
      - la salida con lo que cobra el repartidor y su bitácora sin el enlace ni la dirección; la corrección conserva cuándo salió; el pedido entregado dice que lo debe Paquetenvia, en el detalle y en la lista;
      - rechazos: un billete que no alcanza, «paga con» sin cobrar, cobrar un pedido pagado, uno entregado, uno de mostrador y uno de otro negocio (404); un pedido pagado sale sin cobrar nada; el validador;
      - «paga con» en Vender y en el menú, rechazado debajo del total; un pedido del menú sale solo confirmado;
      - «En camino» por WhatsApp una vez, con el enlace si lo hay y sin él; no por anotar el viaje, por una corrección ni por un pedido entregado antes;
      - `OrderUpdated` en tiempo real;
      - lo que debe el repartidor (el saldo, también con un pago parcial por transferencia) y lo que lleva en camino, solo del negocio;
      - la liquidación cobra los pedidos en la caja propia (no en la del compañero) y mete el viaje y la comisión como gastos y el redondeo como depósito; el corte cuadra con lo entregado; no se liquida dos veces ni con un saldo que cambió desde que se leyó la lista (un pago parcial por transferencia);
      - sin caja propia, pedidos que no se deben (en camino, cobrados en mostrador, sin domicilio o inexistentes) o que no cuadran, rechazada sin guardar nada;
      - por transferencia, aparte del efectivo de la caja;
      - el historial más reciente primero, el detalle y nada de otro negocio; los validadores.
    - `UseCaseRulesTests`: mandar el pedido pide `ManageOrders`; leer lo que deben los repartidores, `ManageCash`; liquidar, `ChargeOrders` y además `ManageCash`.
  - **SQL Server:** `CourierSettlementsSqlServerTests` (8 pruebas): la salida se guarda con el pedido y choca (409) con un pago guardado al mismo tiempo; la liquidación se guarda en su negocio con sus pedidos y ajustes en orden; un pedido se liquida una vez: el índice único rechaza una segunda liquidación del mismo pedido aunque la aplicación no haya visto la primera; la caja no se borra bajo su liquidación; las restricciones CHECK; la versión anterior sigue tomando pedidos a domicilio y anotando viajes; revertir detenido con el error 50014 y después solo lo que agregó.
  - **WebApi:** `CrossTenantCourierSettlementsSqlServerTests` (3 pruebas): otro negocio no manda el pedido, no ve lo que debe el repartidor ni su liquidación y no liquida un pedido ajeno, ni con el encabezado del otro; quién del equipo manda, lee y liquida (sin caja propia, no); una pantalla que vio otro saldo (409); una petición sin «cobra en efectivo», con un ajuste vacío, con tarjeta o que no cuadra, rechazada sin guardar nada.
  - **Cliente:**
    - `courierCash.test.ts` y `courierSettlementForm.test.ts`: «paga con» y su cambio con los mensajes del servidor; lo que todavía cobra el repartidor; los ajustes con su signo, el cuadre en centavos, el ajuste que explica la diferencia y lo que se manda (con el total de los pedidos).
    - `OrderDeliveryPanel.test.tsx`: salir con lo que cobra y el cambio, un billete que no alcanza sin mandarlo, un pedido pagado que no cobra, «En camino» y corregirlo, en camino lo que todavía cobra (menos con un pago parcial, nada si ya está pagado), «Paquetenvia te debe», y sin salida para un pedido entregado o pendiente.
    - `OrdersList.test.tsx`: «En camino» y «Lo debe Paquetenvia» en la lista; `OrderUpdated` la lee otra vez sin aviso.
    - `CourierCash.test.tsx` y `CashManagement.test.tsx`: lo que debe, lo que lleva (lo que todavía cobra) y el historial con su detalle; abrir la caja propia antes de liquidar; liquidar con el viaje y un faltante hasta cuadrar; el 409 del servidor; la pestaña aparece cuando un repartidor cobró algo o hay liquidaciones, y liquida solo quien también cobra.
    - `SellPage.test.tsx`, `PublicMenuPage.test.tsx`, `sellDraft.test.ts`, `ticket.test.ts`, `SaleTicket.test.tsx` y `access.test.ts`: «Paga con» en Vender y en el menú con su cambio y el rechazo (con `aria-invalid`); el borrador lo guarda y una venta de antes conserva su clave; el ticket dice lo que todavía cobra el repartidor; liquidar pide `ManageCash` y `ChargeOrders`.
  - **Escenas visuales** (contra `master` en `516f92b`, con la 22C y las pestañas de sección):
    - **Nuevas (11):** Caja › Repartidores en escritorio, teléfono y oscuro, sin caja propia, liquidar a Paquetenvia con su viaje y un faltante, y el detalle de una liquidación; Vender a domicilio con «paga con» y su cambio; salir a entregar cobrando en efectivo; Pedidos en camino y por liquidar; un pedido entregado que debe Paquetenvia; y el carrito del menú en línea con cuánto paga. No salen de la línea base de `master` hasta que la fase entre a `master`.
    - **Cambian a propósito (6):** Vender a domicilio en escritorio y teléfono y el carrito del menú a domicilio (también por debajo del mínimo), que ahora preguntan con cuánto paga el cliente; y el detalle de un pedido a domicilio y el envío de Paquetenvia registrado, que ahora ofrecen «Salió a entregar».
    - **Resultado:** con esas capturas, las 386 escenas pasaron dos corridas seguidas contra el mismo build. Otra vez contra `master` en `44abb7a` (con las PR #77 a #80): cambian las mismas 6, las 11 nuevas salen iguales y las otras 372 no cambian, 389 en total.
- Fase 22E, servicios a domicilio ([ADR 0020](../adr/0020-home-visits.md)):
  - **Domain:** `HomeVisitTests` (34 pruebas):
    - la dirección de una visita dice qué falta y sigue las reglas de una dirección;
    - la visita guarda la dirección, la zona y su cargo; en el mismo lugar conserva lo acordado aunque la zona cambie, y otras referencias se corrigen sin cotizar aunque la zona ya no llegue; en otra dirección cotiza otra vez; necesita una zona del negocio que llegue hoy al código postal; una cita cerrada no cambia de lugar; volver al negocio quita la visita;
    - la venta completa la cita abierta, toma una ya completada sin venta, rechaza una cancelada o que no se presentó, va una vez salvo que su venta se cancele y necesita la venta;
    - el cargo por visita entra al total como un concepto más con IVA incluido, o con la política de precios antes de IVA; conserva su tasa junto a un servicio con otra; va de un centavo al límite; se agrega una vez a una venta nueva con sus servicios y antes de cobrar; no en una cuenta de mesa, un pedido a domicilio ni de plataforma, y se paga con la venta;
    - el traslado empieza en 30 y va de 0 a 240; los mensajes dicen el código postal y quién necesita el traslado.
  - **Application:**
    - `HomeVisitFlowTests` (15 pruebas):
      - agendar a domicilio toma el cargo de la zona y la bitácora no lleva la dirección; sin zonas o fuera de ellas dice por qué y no agenda; en la misma dirección conserva lo acordado (también con otras referencias), en otra cotiza otra vez y sin visita la quita;
      - el traslado del negocio entre una visita y otra cita de la misma persona;
      - los avisos dicen «a domicilio (CP …)» y el cargo, nunca la dirección, y «más IVA» con precios antes de IVA; el recordatorio de una visita dice «a domicilio (CP …)»;
      - la cotización con la zona y el cargo o por qué no; la ruta del día por persona y en orden de hora, con un día entre 2000 y 2100; el traslado lo lee el equipo y lo cambia el dueño, con su bitácora;
      - la venta cobra la visita y completa la cita, una vez salvo que su venta se cancele (la cita, la lista y la ruta dicen que su venta se canceló); una cita en el negocio o una visita sin cargo solo se completa; una cancelada, que no se presentó o de otro negocio se rechaza sin vender; la venta de una cita es de mostrador y con conexión;
      - el borrador de CFDI factura la visita con la clave del servicio vendido como concepto propio.
    - `AutoReplyDeliveryZonesTests`: un negocio que va a la casa de sus clientes habla del cargo por visita.
    - `UseCaseRulesTests`: la cotización y la ruta piden `ManageAppointments`; el traslado lo lee cualquier miembro y lo cambia el dueño o un admin; todo con el módulo Agenda. `AuthorizationBehaviorTests`: la venta de una cita pide también `ManageAppointments`.
  - **SQL Server:** `HomeVisitsSqlServerTests` (10 pruebas): la visita se guarda y se carga con su cita en su negocio, se mueve en su fila y se va cuando el cliente viene al negocio; la venta se guarda con su cargo y la cita completada; dos cobros guardados a la vez chocan en la versión de la cita, y un cobro choca con la visita movida mientras tanto; las llaves guardan cada visita y cada cargo una vez y las ventas bajo su cita; las restricciones CHECK; el traslado empieza en 30; la versión anterior sigue agendando y guardando la configuración; revertir detenido con el error 50015 y después solo lo que agregó. Las pruebas de migraciones anteriores leen con un contexto sin lo de la 22E (`Support/BeforeHomeVisits`).
  - **WebApi:** `CrossTenantHomeVisitsSqlServerTests` (2 pruebas): un negocio cotiza y agenda solo con sus zonas, filtra y ve solo sus visitas y su ruta, no cobra la cita de otro y cobra la suya una vez, sin la dirección en la bitácora; cada negocio tiene su tiempo de traslado y uno fuera del límite se rechaza.
  - **Cliente:**
    - `homeVisits.test.ts`: los totales con el cargo como el servidor (IVA incluido o no, la tasa del negocio junto a un servicio exento, una visita sin cargo), cómo se dice el cargo, la cita que no se cobra y los enlaces de Maps (diez visitas por ruta) y de llamada.
    - `AppointmentsList.test.tsx`: dónde es cada visita, el filtro «Dónde» y «Cobrar» que abre Vender (no para quien no vende, ni en una cita con venta; sí cuando esa venta se canceló); la cotización con cinco dígitos y el mensaje del servidor; la cita se agenda con la dirección; en la misma dirección dice lo acordado sin cotizar, y con otra avisa del nuevo lugar.
    - `VisitRoute.test.tsx`: las visitas de cada persona en orden, con sus cargos, los mapas y la llamada; «Cobrar» solo en la que no tiene venta y para quien vende.
    - `HomeVisitTravelCard.test.tsx` y `DeliveryZones.test.tsx`: el traslado guardado, guardarlo y el límite antes de mandarlo; la tarjeta solo con Agenda.
    - `SellPage.test.tsx`, `sellDraft.test.ts` y `ticket.test.ts`: Vender trae el cliente, el servicio y el cargo de la cita y la manda con el total; deja de ser el cobro de la cita cuando el cajero lo dice; no trae una cita cancelada ni una con venta (sí si esa venta se canceló); el borrador la guarda como otra venta; el ticket dice el cargo por visita.
  - **Escenas visuales** (`agenda-domicilio.spec.ts`, contra `master` en `5aac2b7`, con la 22D y la PR #81):
    - **Nuevas (14):** la agenda con solo las visitas y la ruta del día, en escritorio, teléfono y oscuro; la cita nueva a domicilio con su zona y su cargo, en escritorio, teléfono y oscuro, y fuera de las zonas; lo acordado al editar una visita; Vender cobrando una cita y su venta con el cargo por visita; y el traslado en Zonas de entrega. Los datos simulados compartidos (`mocks/routes-p8.mjs`) contestan la cotización, la ruta, una cita y el traslado. No salen de la línea base de `master` hasta que la fase entre a `master`.
    - **Cambian a propósito (13):** la Agenda en escritorio, teléfono y oscuro («Ruta del día», el filtro «Dónde» y «Cobrar» en las citas sin venta), los 4 formularios de cita nueva (el grupo «Dónde») y 6 escenas de Zonas de entrega (la tarjeta «Visitas a domicilio» debajo de las zonas).
    - **Resultado:** con esas capturas, las 403 escenas pasaron dos corridas seguidas contra el mismo build.
- Fase 23A, registro confiable:
  - **Application:**
    - `AccountEmailTests` (9 pruebas): registrarse con el correo de una cuenta confirmada responde igual, no crea nada y solo avisa a su dueño, después de responder (a una cuenta de cliente, que un negocio va con otro correo); con una cuenta empezada y sin activar dice que lo llenado no se guardó y su enlace activa la primera con su contraseña de entonces, y con una desactivada nada; registrarse con el correo de otro y entrar con la contraseña propia responde lo mismo exista o no la cuenta; una contraseña fuera de la regla recibe la misma respuesta exista o no la cuenta; la recuperación por correo sale después de responder y solo a una cuenta confirmada, con un enlace que restablece la contraseña; el enlace nuevo solo va a una cuenta sin activar; un correo pedido hace más de un día ya no sale.
    - `RegisterBusinessCommandTests`, `RegisterCustomerCommandTests` y `EmailConfirmationTests`: el enlace de activación sale de la cola una vez, también cuando la transacción se reintentó, y nunca cuando el registro se deshizo. `TeamInvitationTests`: la invitación sale de la cola con un enlace propio que vale junto al que se comparte, una invitación aceptada antes de que salga el correo ya no se envía, y quien fue invitado y se registra o pide otro enlace de activación recibe su invitación. `CustomerPhoneOwnershipTests`: un teléfono confirmado recibe la misma respuesta exista o no la cuenta del correo. `LoginCommandTests`: una cuenta sin activar responde como una contraseña incorrecta, aun con la correcta.
    - La cola de las pruebas (`RecordingOutbox`) guarda los eventos con el `OutboxWriter` real en la transacción de la petición, y `DispatchAsync` solo atiende los que se guardaron: un intento deshecho no manda nada.
  - **Infrastructure:** `EmailServiceLoggingTests` (con un servidor SMTP falso): un rechazo pasajero (451), sin conexión o del remitente (530 por credenciales, 550 5.7.1 de un relay) lanza `EmailDeliveryException` sin la dirección ni la respuesta del servidor, para que la cola reintente; un 550 al destinatario solo queda en el log; con los valores de ejemplo, ajustes incompletos o un remitente que no es correo no se intenta enviar; en Development, sin proveedor, el log muestra el correo para abrir su enlace; el aviso de cuenta existente dice cómo entrar y que la cuenta no cambió (a una de cliente, que un negocio va con otro correo), y el de la cuenta empezada, que lo llenado no se guardó y que no abra el enlace si no la empezó. `EmailProviderHealthCheckTests`: Healthy solo con ajustes completos (un relay sin usuario no necesita contraseña), y Degraded sin revelar sus valores. `OutboxTests`: el evento nuevo pasa por el serializador de la cola.
  - **WebApi:** `AccountEmailsSqlServerTests`: sobre la API real, registrarse con el correo de una cuenta responde lo mismo que con uno nuevo, y solo su dueño recibe el aviso. `HealthEndpointsTests`: `/health/ready` trae `"email-provider"`, Degraded con los valores de ejemplo de `appsettings.json`, sin fallar. La suite entre negocios corre la cola para abrir el enlace de activación.
  - **Cliente:** `CheckEmail.test.tsx`: al venir del registro, «Revisa tu correo» dice que quien ya tenía cuenta recibe cómo entrar, sin nombrar la dirección; desde el enlace del login no lo dice.
  - **Escenas visuales** (contra `master` en `2acbc81`): ninguna cambia. La línea nueva de «Revisa tu correo» solo sale al venir del registro, y sus 3 escenas abren la página directo (en la etapa 3 salía siempre y las cambiaba). Las 403 escenas pasaron dos corridas seguidas contra el mismo build.


- Fase 24A, Explorar ([ADR 0021](../adr/0021-business-directory.md)):
  - **Domain:** `BusinessDirectoryTests` (32 pruebas):
    - los números guardados de las categorías y `BusinessCategoryDefaults.Suggest` para cada giro de la lista con cada tipo, sin acentos, mayúsculas ni espacios, y con el tipo cuando el giro no es de la lista;
    - la categoría sigue al giro hasta que el dueño elige otra o decide la suya, y entonces un giro nuevo (también uno escrito a mano) no la mueve;
    - la descripción se recorta, cabe en 160 caracteres y va en un solo renglón (sin tabuladores, saltos ni separadores de línea o de párrafo), y un error no cambia nada;
    - «Aparecer en Explorar» empieza encendido.
  - **Application:** `PublicDirectoryTests` (15 pruebas):
    - quién aparece y quién no: oculto, menú apagado, sin productos, solo servicios, categoría inactiva y suspendido;
    - cada tarjeta responde lo mismo que el menú de su negocio, con la caja en la principal, en otra sucursal y de antes de las sucursales, y cerrado por horario;
    - el orden;
    - la ciudad, salvo en una cocina que solo entrega, también al buscarla;
    - la búsqueda sin acentos dentro de la categoría y por páginas, con conteos que no dependen de la búsqueda;
    - la foto de 30 segundos, también con el reloj que va hacia atrás;
    - la regla `OnlineOrderAvailability`, con la mesa fuera del horario y en un día cerrado;
    - el dueño: un cambio auditado sin el texto de la descripción, nada al guardar igual, y por qué no aparece;
    - los validadores, con la página 1001 y los separadores de línea.

    `UpdateMyTenantCommandTests`: el giro mueve la categoría hasta que el dueño guarda su tarjeta, aunque la deje en la que el giro le dio; después, un giro nuevo solo la sugiere.

    Las pruebas del menú en línea (`OnlineMenuTests`, `SpecialDaysMenuTests`, `OpeningHoursFlowTests`, `HomeDeliveryFlowTests`) siguen iguales con la regla compartida. `AuthorizationBehaviorTests` suma `GetPublicDirectoryQuery` a los anónimos.
  - **SQL Server:**
    - `BusinessDirectoryMigrationSqlServerTests` (4 pruebas): el relleno da a cada giro y tipo la categoría de `Suggest`; la versión anterior abre y edita negocios sobre las columnas nuevas; las CHECK; revertir se detiene con el error 50016 por un negocio oculto, por una descripción y por un cambio solo en la bitácora, y después quita solo lo suyo.
    - `PublicDirectorySqlServerTests`: la consulta de Explorar sin negocio en contexto lee cada negocio de sus propias filas: caja de la principal, de otra sucursal y de antes de las sucursales, día especial cerrado, sin local, zonas, oculto, solo servicios, categoría inactiva y menú apagado.
    - Las pruebas de las migraciones anteriores siembran y leen con el modelo sin las columnas nuevas (`Support/BeforeBusinessDirectory`, que también ignora `BeforeHomeVisits`).
  - **WebApi:** `CrossTenantDirectorySqlServerTests`, sobre la API real:
    - un visitante solo encuentra el negocio que se mostró, con lo que decidió su dueño y sin su correo ni su id;
    - el otro negocio, aun con el encabezado del primero, solo cambia el suyo;
    - la tarjeta del dueño dice «oculto» y «sin productos»;
    - cada cambio queda en la bitácora de su negocio.

    `TenantFilterBypassAllowlistTests` suma los dos `IgnoreQueryFilters()` de `PublicDirectoryFeature.cs`.
  - **Cliente:**
    - `ExplorePage.test.tsx`: las tarjetas con lo que responde el menú, las categorías con negocios, la búsqueda en la dirección, los estados vacíos, «Ver más» y una categoría que no existe.
    - `CustomerLayout.test.tsx`: visitante, cliente y equipo.
    - `HomeRedirect.test.tsx`: visitante y cliente a Explorar; negocio nuevo a su guía.
    - `DirectoryCard.test.tsx`: guardar, la sugerencia del giro, la vista previa, por qué no aparece y un solo renglón.
    - `businessCategories.test.ts`: cada categoría del contrato (`BusinessCategory` generado) una vez, con su propia ruta y Otros al final; buscarlas por ruta y por valor; y `orderStatus` con `closed` del servidor.
    - `access.test.ts`: el inicio de un cliente es `/explorar`.
  - **Escenas visuales** (contra `master` en `c3c1a00`):
    - **Nuevas (8, `explorar.spec.ts`):** Explorar en escritorio, teléfono y oscuro; una categoría en el teléfono; una búsqueda sin resultados; sin negocios todavía; como cliente en el teléfono; y la tarjeta «Cómo te ven en TienditApp» con el menú publicado. Los datos simulados (`mocks/routes-p9.mjs`) contestan el directorio y el perfil. No salen de la línea base de `master` hasta que la fase entre a `master`.
    - **Cambian a propósito (19):**
      - Mis pedidos en escritorio, teléfono y oscuro (3): ahora van en el marco del cliente, con «Explorar» y «Mis pedidos».
      - Mi negocio (10): la tarjeta «Cómo te ven en TienditApp» al final de Configuración › Negocio, en escritorio, teléfono y oscuro, al editar (también en el teléfono y en oscuro), con días especiales, la vista previa de un tema, el menú en línea y las plataformas de reparto.
      - La consola de plataforma y los planes, en escritorio, teléfono y oscuro (6): la campana de avisos ya no sale al administrador de la plataforma, que no es equipo de un negocio (`Topbar`).
    - **Resultado:** con esas capturas, las 411 escenas pasaron contra el mismo build, y las 8 de Explorar dos corridas seguidas. «Zonas de entrega: tiempo de traslado entre visitas» (`agenda-domicilio.spec.ts`) salió distinta en una de las dos corridas completas, como en la 23A, y la fase no toca esa pantalla. Repetida 15 veces sola falla 3, siempre con la barra superior y el menú unos 6 px más abajo que el contenido: la página queda desplazada al tomar la captura. Queda para un cambio propio de las escenas.
- Fase 24B, Comprar con tu cuenta ([ADR 0022](../adr/0022-shopping-with-an-account.md)):
  - **Domain:** `AccountAddressTests` (10 pruebas): la dirección con las reglas de la de un pedido y lo que falta o está mal; el nombre por omisión y en un renglón corto; la undécima y una sin cuenta se rechazan; cambiarla sigue las mismas reglas y un rechazo no cambia nada; el mismo lugar sin mayúsculas ni espacios de más; y marcarla usada guarda las referencias del pedido, solo para el mismo lugar.
  - **Application:** `CustomerAccountShoppingTests` (8 pruebas):
    - el pedido con la sesión de una cuenta de cliente es de la cuenta sin confirmar el teléfono, y nadie lo reclama; con la del equipo o de una cuenta inactiva es de invitado;
    - la misma llave registra el pedido una vez, responde aunque el negocio ya cerró su caja, no responde a otro teléfono (una llave nueva sin caja se rechaza, como antes) y, cancelado el pedido, dice que el negocio lo canceló; el validador rechaza una llave vacía y un nombre de dirección largo;
    - a domicilio con sesión, guarda la dirección cuando se pide y la ofrece primero; sin sesión no guarda nada;
    - las direcciones son solo de su cuenta, una por lugar y hasta 10, con los límites de la dirección de un pedido.

    `PublicDirectoryTests` (+2): con el código postal, cada tarjeta de un negocio que entrega dice si llega y cuánto cobra, con la zona activa y sin cambiar la lista ni la foto compartida; el código postal lleva 5 dígitos.

    `DeliveryZonesSqlServerTests` cuenta el índice nuevo `IX_DeliveryZonePostalCodes_PostalCode` (no único). Las pruebas del menú en línea pasan la sesión (`fixture.CurrentUser`). `AuthorizationBehaviorTests` cuenta los cuatro casos de uso de las direcciones (254).
  - **SQL Server:** `AccountAddressesMigrationSqlServerTests` (2 pruebas): la CHECK del código postal y que las direcciones se van con la cuenta; revertir se detiene con el error 50017 sin cambiar nada mientras haya una dirección, y después quita solo la tabla y el índice, otra vez no hace nada y hacia adelante vuelven. `rollback.sql` es el que genera EF desde el `Down` de la migración.
  - **WebApi:** `CrossTenantCustomerAccountsSqlServerTests` (3 pruebas), sobre la API real con dos negocios:
    - una cuenta no lee, cambia ni borra las direcciones de otra; el equipo de un negocio no guarda direcciones y sin sesión responde 401;
    - el pedido con sesión queda en la cuenta en los dos negocios y en ninguna otra; la misma llave es otro pedido en otro negocio; sin sesión, la misma llave responde «Ese pedido ya se envió.» sin el folio; Mis pedidos manda el producto de cada línea; no se crea cliente temporal; la dirección queda usada;
    - Explorar con el código postal dice quién llega y cuánto cobra, sin el nombre de la zona, y rechaza un código postal de 4 dígitos.

    `TenantFilterBypassAllowlistTests` cuenta el tercer `IgnoreQueryFilters()` de `PublicDirectoryFeature.cs`; `ArchitectureTests` lista `AccountAddress` entre las entidades sin negocio.
  - **Cliente:**
    - `savedCart.test.ts` (4): un carrito por tienda hasta enviarlo o vaciarlo, 7 días, lo que no se lee, las líneas de un pedido en piezas enteras (y cuántas se piden por peso), y lo que el menú de ahora puede tomar.
    - `PublicMenuPage.test.tsx` (+4): el carrito guardado con el menú de ahora y lo que quedó fuera (lo que ya no está y lo que se pide por peso); la llave igual al reenviar y el carrito olvidado al registrar; los datos y la última dirección de la cuenta, y guardar una nueva; el equipo pide como invitado.
    - `ExplorePage.test.tsx` (+2): el código postal se pide una vez, con el foco en el campo y de vuelta en el botón, y cada tarjeta dice si llega; el aviso del carrito sin enviar.
    - `MyOrders.test.tsx` (2): «Pedir otra vez» llena el carrito y abre la tienda; un pedido todo por peso abre la tienda sin tocar el carrito que había.
    - `AccountPage.test.tsx` (3), `phones.test.ts` (1) y `businessCategories.test.ts` (+1, el envío como los precios del negocio). `CustomerLayout.test.tsx` pide el ícono «Mi cuenta».
  - **Escenas visuales** (contra `master` en `d7e54f5`):
    - **Nuevas (14, `cuenta-cliente.spec.ts`):** Cuenta en escritorio, teléfono y oscuro; agregar y borrar una dirección; sin direcciones; Explorar con código postal y un carrito sin enviar (escritorio y teléfono), el formulario del código postal y «Seguir con el pedido»; la tienda con la cuenta a domicilio, con otra dirección y con el pedido enviado; y «Pedir otra vez» con un producto agotado. Los datos simulados son `mocks/routes-p10.mjs` (direcciones), el directorio con `postalCode` (`mocks/routes-p9.mjs`) y Mis pedidos con el producto de cada línea (`mocks/routes-p4.mjs`). No salen de la línea base de `master` hasta que la fase entre a `master`.
    - **Cambian a propósito (9):**
      - Explorar en escritorio, teléfono y oscuro, una categoría, una búsqueda sin resultados, sin negocios y como cliente (7): la fila «¿Te lo llevan a domicilio? Escribe tu código postal».
      - El pedido enviado del menú público, para llevar y a domicilio (2): la línea «¿Pides seguido? Crea tu cuenta» para un visitante.
    - **Resultado:** con esas capturas, las 425 escenas pasaron contra el mismo build, y las 14 nuevas y las 8 de Explorar, dos corridas seguidas. Mis pedidos no cambia: «Pedir otra vez» se ve igual como botón que como enlace.
- Fase 24C, Seguimiento ([ADR 0023](../adr/0023-order-tracking.md)):
  - **Domain:** `OrderTrackingTokenTests` (11 pruebas): el pedido empieza sin enlace y guarda el que se le da, una sola vez; un enlace corto, largo, en Base64 normal, con relleno, espacios o acentos se rechaza sin cambiar nada.
  - **Application:**
    - `OrderTrackingStepsTests` (9): los pasos de una cocina, una tienda y un servicio con la primera hora de cada uno; En camino solo a domicilio, con la hora de salida; los pasos saltados hechos sin hora; un pedido entregado o terminado con todo hecho; un estado que regresa conserva la primera hora; un cancelado hasta donde llegó, también si ya había salido; los 30 días; y que cada enlace nuevo tiene la forma de uno y es distinto.
    - `OrderTrackingQueriesTests` (7): el pedido en línea lleva su enlace, el mismo al reenviarse con su llave; el enlace muestra el pedido desde cualquier negocio en contexto sin el nombre, el teléfono, la dirección ni las notas del cliente; uno desconocido, con otras mayúsculas, incompleto, vencido o de un negocio suspendido no se encuentra (el último día sí); el detalle es solo de la cuenta, con su dirección y sus pasos; una visita a domicilio lleva su cargo dentro del total; Mis pedidos separa en curso y anteriores (un pedido abierto de más de 30 días es anterior); y los validadores.

    `AuthorizationBehaviorTests` agrega el seguimiento a los anónimos (256 casos de uso). Las pruebas de Mis pedidos pasan el reloj.
  - **SQL Server:** `OrderTrackingMigrationSqlServerTests` (3 pruebas): los pedidos ya guardados quedan sin enlace y la versión anterior sigue creando pedidos; la CHECK de la longitud y el índice único entre negocios; revertir quita solo la columna, la CHECK y el índice, otra vez no hace nada y hacia adelante el pedido sigue, sin enlace. `BeforeOrderTracking` es el modelo anterior para las pruebas de migraciones viejas.
  - **WebApi:** `CrossTenantOrderTrackingSqlServerTests` (1 prueba), sobre la API real con dos negocios: cada enlace abre su pedido en su negocio, sin sesión y sin datos del cliente; el mismo enlace con otras mayúsculas y uno desconocido responden el mismo 404; el detalle es solo de la cuenta (otra cuenta, el equipo del negocio y el pedido de un invitado dan 404; sin sesión, 401); y En curso trae el pedido, Anteriores no. `TenantFilterBypassAllowlistTests` cuenta los `IgnoreQueryFilters()` de `GetMyOrderQuery.cs` y `GetPublicOrderTrackingQuery.cs`, y `CrossTenantSuiteCoverageTests` ve el controller nuevo en la suite.
  - **Cliente:**
    - `OrderTimeline.test.tsx` (3): los pasos en orden con su estado para un lector de pantalla, uno actual, la hora (o la fecha si es de otro día) y los colores del chip.
    - `OrderItemsSummary.test.tsx` (2): la visita a domicilio, el IVA si los precios no lo incluyen, «Por pagar» y «Pagado».
    - `MyOrderPage.test.tsx` (6): el detalle, «Pedir otra vez», el cancelado con su motivo, el pedido de otra cuenta, preguntar cada 20 segundos hasta un 404 (con lo último que vio) y no preguntar por uno terminado.
    - `OrderTrackingPage.test.tsx` (4): el enlace del fragmento en el cuerpo, sin enlace no pregunta, el enlace vencido, y el enlace que vence con la página abierta.
    - `MyOrders.test.tsx` (5, +3): En curso y Anteriores con su paso y «Ver pedido», sin pedidos, y Anteriores que se vuelve a pedir cuando un pedido sale de En curso.
    - `orderTracking.test.ts` (2) y `CopyTrackingLink.test.tsx` (2). `PublicMenuPage.test.tsx`: «Seguir tu pedido» en el recibo, y «Copiar enlace de seguimiento» solo para un invitado.
  - **Escenas visuales** (contra `master` en `29a81a8`):
    - **Nuevas (11, `seguimiento.spec.ts`):** el detalle de un pedido en camino en escritorio, teléfono y oscuro, uno cancelado y uno de otra cuenta; «Ver pedido» desde Mis pedidos; el seguimiento con enlace en escritorio, teléfono y oscuro, sin enlace y con uno vencido. Los datos simulados son `scenes/seguimiento.mocks.ts`. No salen de la línea base de `master` hasta que la fase entre a `master`.
    - **Cambian a propósito (5):**
      - Mis pedidos en escritorio, teléfono y oscuro (3): las secciones En curso y Anteriores, el paso de cada pedido y «Ver pedido».
      - El pedido enviado del menú público a domicilio y el de la tienda con la cuenta (2): «Seguir tu pedido» como acción principal, «Copiar enlace de seguimiento» para un invitado y «Avisar por WhatsApp» en verde suave.
    - **Resultado:** la comparación contra `master` dio 421 escenas iguales, las 10 nuevas con captura sin línea base y las 5 que cambian. Con esas capturas, las 436 escenas pasaron contra el mismo build, y los cuatro archivos tocados (seguimiento, Vender y Pedidos, cuenta del cliente y pantallas públicas: 170 escenas) dos corridas seguidas.
- Fase 25, Fotos del servicio ([ADR 0024](../adr/0024-service-photos.md)):
  - **Domain:** `ServiceOrderPhotoTests` (15 pruebas): la foto guarda etapa, nota, quién (nombre y cuenta) y cuándo, y marca la orden, y su evento lleva el archivo; los blancos quedan vacíos y un nombre largo se corta; la nota hasta 300; hasta 20 por etapa; el mismo archivo una vez; sin archivo o con una etapa que no existe se rechaza; solo una orden de servicio, no cancelada; una terminada recibe la foto de la entrega y no una del antes; quitar mientras la orden está abierta con su evento, una que no está y las de una orden terminada o cancelada, que se conservan.
  - **Application:**
    - `ServiceOrderPhotosTests` (14): el archivo propio del negocio con quién y cuándo; la dirección anterior de un archivo propio como su ruta privada; otra dirección, otro negocio, el área de comprobantes o una ruta que sube de carpeta se rechazan, y también una clave con la forma correcta que no está en el almacenamiento; por qué una orden cancelada o una venta no lleva fotos; quitar solo quien la tomó o un administrador, y una orden terminada conserva sus fotos aunque sea un administrador y solo recibe del después; primero las de la recepción (sin una dirección ajena ni un archivo de otro negocio) y luego las agregadas en orden; una cancelada se ve sin ofrecer más; tres intentos cuando la orden cambia cada vez, y luego lo dice; y el validador.
    - `UseCaseRulesTests` agrega los tres casos de uso y la subida abierta al tablero con el módulo de servicios (259 con regla en `AuthorizationBehaviorTests`).
  - **SQL Server:**
    - `ServiceOrderPhotosMigrationSqlServerTests` (3): la CHECK de la etapa, el filtro del negocio y el borrado con la orden; la versión anterior (`BeforeServicePhotos`) sigue creando y terminando órdenes sobre la tabla; revertir se detiene con 50018 si hay fotos y, sin ellas, quita solo la tabla, otra vez no hace nada y hacia adelante vuelve vacía.
    - `ServiceOrderPhotosSqlServerTests` (3), con `SavedMeanwhile`: una foto agregada a la vez se queda con la de la petición; la vigésima agregada a la vez deja sin lugar a otra; y una orden terminada a la vez conserva la foto que se quería quitar.
    - `OutboxTests` agrega los dos eventos de foto a la ida y vuelta del serializador.
  - **WebApi:** `CrossTenantOperationsSqlServerTests.ServicePhotos_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable` (1), sobre la API real: para otro negocio la orden ajena no existe (404 al ver, al agregar con una foto suya, al quitar y al abrir el archivo), no usa el archivo ajeno en una orden suya y con los suyos sí; las fotos del primero quedan iguales.
  - **Cliente:** `ServicePhotosPanel.test.tsx` (9): antes y después con su nota y quién, «Quitar» solo donde se puede; sin fotos; subir y agregar con etapa y nota; una orden entregada solo ofrece el después, y la foto se elige sin `capture`; pedir la foto, mostrar el error del servidor y reintentar sin subirla otra vez; lo leído se queda con «Reintentar» si volver a pedir falla; quitar solo después de confirmar; una orden cancelada sin formulario; y `describeServicePhoto`. `privateFiles.test.ts` deja la prueba de `parsePhotos`, que se retiró.
  - **Escenas visuales:** cambian a propósito 4: el detalle de una pieza del Taller en escritorio, teléfono y oscuro, y el final del detalle de un servicio en Pedidos, con el panel de fotos en lugar de «Fotos al recibir» y «Fotos al entregar». Las 7 escenas de esos dos archivos que abren un servicio pasaron dos corridas seguidas contra el mismo build.
- Fase 24D-1, Citas en línea ([ADR 0025](../adr/0025-online-booking.md)):
  - **Domain:** `OnlineBookingTests` (43 pruebas): la solicitud ocupa su hora «Por confirmar» con su canal, cuenta, llave y enlace; una del negocio no lleva nada de eso; un enlace con otra forma se rechaza; aceptar la programa, también ya empezada, y no una que terminó; rechazar la cancela; el estado no entra ni sale de «Por confirmar» sin decidirla, y no se cobra antes; el cliente cancela antes de que empiece una que agendó en línea, y no una ya empezada, en servicio, cerrada o del negocio; las citas a la vez de 1 a 20; la duración del catálogo o una hora; las horas libres cada media hora hasta el cierre, sin las ocupadas, mientras sean menos que las citas a la vez, después de la hora mínima, con los días especiales y nada sin horario semanal; una apertura que pasa de la medianoche, hasta el último día ofrecido; y cuántas corren a la vez.
  - **Application:** `OnlineBookingFlowTests` (26): la página con los servicios y su duración, y nada mientras falte algo para agendar; una semana de horas desde una hora después de ahora, hasta el último día, y nada de un servicio que no se agenda; las horas después de la medianoche con su fecha; pedir guarda «Por confirmar» y avisa; una hora ocupada mientras o no ofrecida, el día después del último y el tope de 3 por teléfono se rechazan; la capacidad del negocio; la misma solicitud otra vez responde la guardada; aceptar y rechazar avisan, aceptar ya empezada no avisa y una terminada se rechaza; moverla avisa que sigue por confirmar; cambiar el estado la acepta o la rechaza; el enlace muestra y cancela, y uno desconocido o cambiado no encuentra nada; la cuenta ve solo las suyas en todos los negocios, cancela solo las suyas y lee «No se confirmó» una que nadie decidió; los ajustes dicen lo que falta y quedan en la actividad; y la campana cuenta las que esperan, las de hoy urgentes.

    `AuthorizationBehaviorTests` agrega los cinco anónimos (270 casos de uso con regla). `UseCaseRulesTests` pide el módulo de agenda en las del negocio; las públicas y de la cuenta viven en `Application.OnlineBooking`. `TenantFilterBypassAllowlistTests` cuenta los tres `IgnoreQueryFilters()` de `CustomerAppointmentsFeature.cs`.
  - **SQL Server:**
    - `OnlineBookingMigrationSqlServerTests` (4): las citas guardadas quedan del negocio y la versión anterior sigue agendando; las CHECK y los índices filtrados únicos; revertir se detiene con 50019 si hay una cita en línea y, sin ellas, quita solo lo agregado, otra vez no hace nada y hacia adelante vuelve. `BeforeOnlineBooking` es el modelo anterior para las pruebas de migraciones viejas.
    - `OnlineBookingSqlServerTests` (3), con el candado real: cinco solicitudes a la vez por la última hora dejan una; la misma solicitud dos veces a la vez es una cita con el mismo recibo; y tres solicitudes de un teléfono que ya tiene dos esperando, detenidas detrás del candado del negocio, dejan pasar solo una (sin el segundo conteo pasan las tres).
  - **WebApi:** `CrossTenantOnlineBookingSqlServerTests` (2), sobre la API real con dos negocios: cada página ofrece solo sus servicios, la solicitud queda en su negocio y su enlace la abre ahí; el otro negocio no la lee, no la acepta ni la rechaza, y sus ajustes son suyos; un enlace desconocido da 404; la cuenta ve y cancela solo las suyas (otra cuenta y el equipo del negocio dan 404; sin sesión, 401). `CrossTenantSuiteCoverageTests` ve el controller nuevo en la suite.
  - **Cliente:**
    - `PublicBookingPage.test.tsx` (6): agendar como invitado con el enlace de la cita; la semana siguiente y de vuelta; una respuesta perdida deja la misma solicitud con su llave, y una hora ocupada pide otra; la cuenta con su nombre y teléfono y Mis citas; el día de una hora después de la medianoche; y un negocio que no agenda.
    - `AppointmentTrackingPage.test.tsx` (4), `MyAppointments.test.tsx` (3), `OnlineBookingCard.test.tsx` (4) y `onlineBooking.test.ts` (4).
    - `AppointmentsList.test.tsx` (8, +4): decidir una solicitud (aceptar, rechazar después de confirmar, sin cobrar), el aviso con «Ver solicitudes», abrir desde la campana ya filtrada y «La canceló el cliente».
    - Más: `CopyTrackingLink.test.tsx` (el enlace de una cita), `attention.test.ts`, `businessCategories.test.ts`, `homeVisits.test.ts` (no se cobra antes de aceptarla), `PublicMenuPage.test.tsx` («Agenda tu cita», no en una mesa) y `ExplorePage.test.tsx` (la insignia).
  - **Escenas visuales** (contra `master` en `d21f0f2`):
    - **Nuevas (28, `citas-en-linea.spec.ts`):** agendar (servicios y horarios en escritorio, teléfono y oscuro, tus datos, la solicitud enviada, la semana siguiente, un negocio que no agenda y con la cuenta); «Agenda tu cita» en el menú y la insignia de Explorar; tu cita con enlace en tres vistas, cancelar y sin enlace; Mis citas en tres vistas; las solicitudes en la agenda en tres vistas, el aviso y rechazar; y la tarjeta de Configuración apagada y prendida. Los datos simulados son `mocks/routes-p12.mjs` (ajustes y decisión) y `scenes/citas-en-linea.mocks.ts`; el mock de la agenda filtra por estado. No salen de la línea base de `master` hasta que la fase entre a `master`.
    - **Cambian a propósito (37):**
      - La Agenda y sus diálogos, también la de domicilio (13): el selector «Estado» es un poco más ancho por la opción «Por confirmar».
      - Configuración del negocio (10): la tarjeta «Citas en línea» (el negocio de las escenas tiene agenda).
      - Las pantallas del cliente con sesión (14): Cuenta, Explorar, el detalle de un pedido y Mis pedidos llevan «Mis citas» en el marco.
    - **Resultado:** la comparación contra `master` dio 399 escenas iguales, las 28 nuevas sin línea base y las 37 que cambian. Con esas capturas, las 464 escenas pasaron dos corridas seguidas contra el mismo build.
- Fase 24E, El menú con la forma de las apps de entrega ([ADR 0026](../adr/0026-store-like-delivery-apps.md)):
  - **Domain:** `MenuCoverTests` (2 pruebas): la portada guarda la dirección base de sus variantes y se quita con un valor vacío; cambiarla no toca el logo.
  - **Application:**
    - `TenantFileIsolationTests` (4): subir la portada la guarda en `covers/{tenantId}` y borra la anterior propia solo después de guardar la nueva, sin tocar el logo; quitarla borra sus variantes y deja el logo; y una dirección que apunta a la carpeta de otro negocio no borra sus archivos, al reemplazar ni al quitar.
    - `OnlineMenuTests.Menu_ShouldCarryItsStoreHeader_TheCoverTheLineAndTheCategory` (1): el menú público trae la portada en 1024 y 2048 px, la descripción y la categoría del negocio, y la foto grande de cada producto.
    - `UseCaseRulesTests` agrega subir y quitar la portada, del dueño o administrador (272 casos de uso con regla en `AuthorizationBehaviorTests`).
  - **SQL Server:** `BusinessCoverMigrationSqlServerTests` (2): los negocios guardados quedan sin portada y la versión anterior (`BeforeBusinessCover`) los sigue guardando; revertir se detiene con 50020 sin cambiar nada mientras un dueño tenga portada y, sin ella, quita solo la columna. `BeforeOnlineBooking` ignora también la portada, y las pruebas de la 24D usan el modelo anterior a ella.
  - **WebApi:**
    - `AccountEndpointsTests` fija las dos rutas nuevas de `MyTenantController` (`POST` y `DELETE /api/v1/auth/my-tenant/cover`).
    - `CrossTenantHttpSqlServerTests.MenuCover_WithTheHeaderOfAnother_ShouldChangeAndRemoveOnlyTheirOwn` (1), sobre la API real: con el encabezado de otro negocio, subir y quitar la portada cambia solo la del propio; la del otro sigue igual y se sigue sirviendo. La prueba quita al final las portadas que subió.
  - **Cliente:**
    - `PublicMenuPage.test.tsx` (30, +5 en «con la forma de las apps de entrega»): la portada, la línea del negocio y sus destacados, y la flecha de regreso solo desde la app; buscar por cualquier palabra del nombre o la descripción sin acentos; una pestaña lleva a su sección y la primera se marca arriba de la página; la hoja del producto con su foto, alérgenos y opciones, que agrega cuántos con instrucciones; y el «+» que agrega de una vez, y solo muestra el producto mientras el menú no recibe pedidos. Las pruebas anteriores siguen con los textos nuevos («Para recoger», «Hacer pedido», «Tus productos»), y la de una mesa comprueba que no aparece «¿Cómo lo quieres?».
    - `savedCart.test.ts` (+3): la misma nota suma, otra nota es otra línea; una línea con una nota que no es texto no se lee; y «Pedir otra vez» trae cada producto con su nota, sin confundir una nota con opciones.
    - `MyOrderPage.test.tsx`: «Pedir otra vez» guarda la nota de cada producto.
    - `storeMenu.test.ts` (4): comparar sin acentos, mayúsculas ni espacios de más; buscar con todas las palabras en el orden del menú; destacar lo marcado, en el orden del menú y hasta su tope, en cuanto uno tiene foto; y el porcentaje de descuento solo cuando de verdad cuesta menos.
    - `MenuCoverCard.test.tsx` (2): subir una portada con su vista previa con el logo; cambiar o quitar la que tiene, y decir por qué cuando no se puede.
    - `PublicBookingPage.test.tsx` (6) sigue pasando con la barra y las filas de la tienda.
  - **Escenas visuales** (contra `master` en `108da45`):
    - **Nuevas (14, `menu-tienda.spec.ts`):** la tienda con portada en escritorio, teléfono y oscuro; la hoja del producto en teléfono y escritorio; buscar; las pestañas a media página; la información del negocio; el botón del pedido; el pedido en teléfono y escritorio; y la tarjeta «Portada del menú» sin foto y con foto. Los datos simulados son `scenes/menu-tienda.mocks.ts` y `mocks/img/portada.svg`; el menú simulado trae las fotos grandes, la portada, la descripción y la categoría. No salen de la línea base de `master` hasta que la fase entre a `master`.
    - **Cambian a propósito (51):**
      - El menú en línea y sus flujos (30): la tienda en cada tema, vista y modo, el carrito, el pedido a domicilio, el recibo, cerrado, un día especial, una mesa, una cocina sin local y la tienda con tu cuenta.
      - La página de citas (8), con la barra, las filas y el botón de la tienda.
      - Configuración del negocio (13): la tarjeta «Portada del menú» y la línea nueva de la tarjeta de Explorar.
    - **Resultado:** la comparación contra `master` dio 413 escenas iguales, las 14 nuevas sin línea base y las 51 que cambian. Con esas capturas, los seis archivos tocados (243 escenas) pasaron dos corridas seguidas contra el mismo build. Las escenas del carrito detectaron, después de la revisión, que el «+» de un producto sin foto había quedado debajo del área de la fila que abre su hoja (sin layout, Vitest no lo ve).
- Fase 24D-2, Buscadores y vistas previas ([ADR 0027](../adr/0027-search-engines-and-link-previews.md)):
  - **Application:**
    - `PublicPagesTests` (5): la vista previa trae lo que el menú muestra (nombre, línea, categoría, logo, portada, el teléfono del negocio y no el de la sucursal, la dirección en sus partes, el horario de la semana desde el lunes y si aparece en Explorar); una cocina sin local no da su dirección; un menú sin publicar o un negocio que no existe no tienen vista previa; agenda en línea solo con un servicio que agendar; y el sitemap lista lo que lista Explorar, con las categorías que tienen negocios.
    - `AuthorizationBehaviorTests` agrega `GetStorePreviewQuery` y `GetPublicSitemapQuery` a los anónimos (274 casos de uso con regla).
  - **WebApi:**
    - `PublicPagesTests` (21), sobre la API sin base y un mediador falso:
      - una tienda con su título, descripción, canónica sin la mesa del QR, Open Graph y los datos estructurados (tipo, dirección, teléfono, imágenes, horario pasada la medianoche y todo el día);
      - lo que escribe el negocio no sale de su etiqueta, ni en el HTML ni en el JSON;
      - una tienda fuera de Explorar con su vista previa y `noindex`;
      - la página de citas solo listada mientras agenda;
      - una tienda que solo agenda;
      - una que no existe (404) o con un slug que ningún negocio puede tener (404 sin preguntar);
      - una base que no responde: 503 con `Retry-After` y sin `noindex`;
      - Explorar, sus categorías y `/` sin leer las tiendas; una categoría que no existe con `noindex`;
      - las demás pantallas y `/index.html` con `noindex`;
      - `robots.txt` y `sitemap.xml`;
      - la dirección de la petición sin `AppUrl`;
      - y las categorías del servidor iguales a las del cliente.
    - `ClientDeliveryTests` revisa `/explorar` en lugar de una tienda, que ahora lee la base.
  - **Cliente:**
    - `useDocumentTitle.test.tsx` (1): el título mientras la página está abierta, el anterior al cerrarla, y ninguno mientras carga.
    - `storeMenu.test.ts` (+1): el título de una tienda por lo que se hace en ella.
    - `PublicMenuPage.test.tsx` y `PublicBookingPage.test.tsx`: la pestaña lleva el título que escribe el servidor.
  - **Escenas visuales:** no cambian: la fase no toca estilos ni lo que se ve en las páginas.
- Escenas estables, después de la 24A:
  - «Zonas de entrega: tiempo de traslado entre visitas» fallaba 3 de cada 15 veces. Bootstrap enciende `scroll-behavior: smooth`: al escribir en el campo de abajo, la página seguía bajando después de que `snap` la regresaba arriba, y la captura salía 2, 6 u 11 px abajo.
  - La init script de `client/visual/fixtures.ts`, que ya quitaba transiciones y animaciones, quita también el desplazamiento suave. La página vuelve a 0 y se queda ahí en 15 de 15.
  - **Escenas:** contra las capturas anteriores (`master` en `8faed1e`) solo cambia esa, que ahora sale arriba. Con la línea base nueva de `master`, las 411 escenas pasaron dos corridas seguidas, y la escena sola, 15 de 15 dos veces.

`ClientDeliveryTests` (Fase 20) arranca la API real sin base sobre una carpeta como la que deja el publish (`UseWebRoot`, sin el manifiesto de static web assets para que Development no le sume `client/dist`): la copia Brotli o gzip según `Accept-Encoding` (con `q=0` y `*`), HEAD igual que GET, `immutable` en `/assets` solo para lo que se envía (un 412 o 416 sin caché ni codificación), `no-cache` en `/`, en las rutas de la app y en `/index.html`, 404 para un archivo viejo o cualquier copia pedida por su nombre (también `/index.html.gz`), el contrato de la API sin comprimir y las subidas con sus encabezados. `PrivateUploadsGuardMiddlewareTests` bloquea también `/uploads//orders`, `//uploads/orders` y las barras invertidas. En el cliente, `useNewVersion.test.tsx` prueba cada momento en que se pregunta por una versión nueva (pestaña visible, foco, conexión, cada 10 minutos, nunca con la pestaña oculta ni justo al cargar) y `NewVersionNotice.test.tsx`, que sin conexión no ofrece recargar; `BranchesList.test.tsx`, el aviso de que el menú muestra la dirección de la sucursal principal.

Escenas visuales del cliente (Fase 20B, `client/visual`, [README](../../client/visual/README.md)): Playwright sobre el build de producción con la API simulada en modo estricto (una petición sin respuesta o un error de la página hacen fallar la escena). `npm run visual:baseline` toma la línea base de `origin/master` en un worktree y `npm run visual:compare` compara cada escena con cero píxeles de tolerancia. Cubren cada ruta de `App.tsx` (`coverage.spec.ts` lo exige) en escritorio, teléfono y modo oscuro, los diálogos y paneles de cada área, el marco de la app en cinco tamaños, la impresión del ticket y de los cortes, y estados como sin conexión, negocio suspendido o un error de la API: 478 pruebas, contando el muestrario, en estos grupos: Catálogo e Inventario, Vender y Pedidos, Clientes y Mensajes, Caja y Configuración, Reportes › Liquidaciones (desde la 22G), la Agenda a domicilio (desde la 22E), Explorar (desde la 24A), Cuenta y comprar con la cuenta (desde la 24B), el seguimiento del pedido (desde la 24C), las citas en línea (desde la 24D), la tienda del menú en línea (desde la 24E), las pantallas públicas y de cuenta con el marco, el marco en cada tamaño y el anillo de foco. Salen iguales en cada corrida: sin transiciones, animaciones CSS ni desplazamiento suave, esperando las que queden y con repintado de mosaicos completos. Un muestrario de componentes compara además el estilo calculado de cada pieza y las variables CSS que leen nuestros estilos, en claro, en oscuro y en los ocho temas. No corren en CI (se corren antes de abrir un PR que toque el CSS global y el PR dice el resultado); CI comprueba que compilen (`npm run visual:typecheck`) y que la primera visita no pase de `bundle-budget.json` (`scripts/check-bundle.mjs`).

El pipeline HTTP completo solo se prueba en la suite de aislamiento entre negocios (Fases 8 a 15, con hubs, webhook y el acceso de soporte), en las rondas de una cuenta con existencias (Fase 14) y sus platillos cancelados (Fase 15), en el controller de sondeo de `RequestCancellationTests` y en la entrega del cliente (`ClientDeliveryTests`, Fase 20); la integración con SQL Server real cubre folios, proyecciones, ventas, sesiones y aislamiento. El workflow `CI` compila y ejecuta las pruebas, valida migraciones pendientes, aplica las migraciones en SQL Server 2022 con el script y el `sqlcmd` de producción (job `Migrations`, ver [migraciones](database-migrations.md)) y corre lint, pruebas, build y `npm audit` del cliente en cada PR; el despliegue repite build, pruebas y lint antes de publicar. Producción y pruebas apuntan a .NET 10.

## Pirámide esperada

1. **Domain:** invariantes, state machines, cálculos, cancelaciones y pagos.
2. **Application:** handlers, validadores, autorización/capabilities, errores, idempotencia y concurrencia.
3. **Integración:** EF Core contra SQL Server compatible, filtros tenant, migraciones, constraints y persistencia de agregados.
4. **API:** autenticación, autorización, contratos HTTP, archivos, hubs y aislamiento multi-tenant.
5. **Frontend:** componentes críticos, formularios, rutas/guards y estados de error.

## Casos prioritarios faltantes

- Matriz cross-tenant de las áreas nuevas: la suite cubre cada controller con datos del negocio (catálogo, clientes, proveedores, órdenes con reembolsos y anulaciones, archivos privados, inventario con recetas, compras, caja, citas, mesas, sucursales, equipo, campañas y promociones, reportes, Inicio, cocina, tablero de servicios, agentes de IA, los hubs y el webhook), y desde la Fase 13 `CrossTenantSuiteCoverageTests` exige que uno nuevo entre a ella; falta que cada acción tenga su caso.
- Bypass con `IgnoreQueryFilters()`, hubs y webhooks (IDs ajenos, el encabezado `X-Tenant-Id` de un dueño, la ruta de otro negocio y el acceso auditado de `SUPER_ADMIN` ya están en la suite HTTP).
- Concurrencia de stock, pagos, compra y rowversion (los folios ya tienen prueba contra SQL Server).
- Reintentos/idempotencia de movimientos, WhatsApp y eventos post-save (la recepción de compras con llave ya tiene prueba contra SQL Server).
- CORS, cookies y 2FA (refresh, reuso y revocación tienen pruebas desde la Fase 8).
- Validación real de uploads (el borrado confinado por tenant en Blob/local y la descarga autorizada de archivos privados ya tienen pruebas).
- Paginación de los listados con páginas (los topes por negocio de los listados completos, el contrato `ProblemDetails` y la cancelación de requests ya tienen pruebas, y CI verifica el OpenAPI).
- Flujo frontend autenticación/capabilities/permisos.

## Reglas

- Cada bug corregido añade regresión que falla antes del fix.
- Cada nueva regla de dominio tiene pruebas positivas y negativas.
- No debilites assertions ni ocultes tests.
- No uses base compartida, credenciales ni datos reales.
- Tests multi-tenant usan al menos dos tenants y objetos con IDs conocidos.
- Separa tests unitarios de dependencias externas y reserva tests de integración para comportamiento que el provider falso no reproduce.

## Comandos

```powershell
dotnet test tests/FoodParkDigital.Domain.Tests/FoodParkDigital.Domain.Tests.csproj
dotnet test tests/FoodParkDigital.Application.Tests/FoodParkDigital.Application.Tests.csproj
dotnet test tests/FoodParkDigital.Infrastructure.Tests/FoodParkDigital.Infrastructure.Tests.csproj
dotnet test tests/FoodParkDigital.WebApi.Tests/FoodParkDigital.WebApi.Tests.csproj
dotnet test FoodParkDigital.sln
```

Escenas visuales del cliente, desde `client/`:

```bash
npm run visual:baseline   # capturas de origin/master
npm run visual:compare    # este árbol contra ellas
```
