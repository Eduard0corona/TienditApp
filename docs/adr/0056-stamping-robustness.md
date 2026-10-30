# ADR 0056: Robustez del timbrado: el apartado de las colas se renueva, y los CFDI que se quedan timbrando se revisan en el portal del SAT

- Estado: Aceptada
- Fecha: 2026-10-29

## Contexto

La Fase 28K cierra dos huecos del timbrado que quedaron anotados en la deuda técnica.

**El apartado de las colas vencía a mitad del trabajo.** El outbox, el inbox del webhook y la cola de mensajes (ADR 0006) apartan un lote con un `LockToken`, que es token de concurrencia, por `LeaseSeconds` (120 s), y lo trabajan en orden. Un handler lento, como el correo de un CFDI (ADR 0049), que lee el XML, arma el PDF y espera al servidor de correo, o un lote entero de ellos, puede pasar del apartado. Entonces otra instancia (dos en App Service, o el slot de staging durante un despliegue) toma lo que venció y lo vuelve a ejecutar: el cliente recibe dos veces el correo del CFDI o el mensaje de WhatsApp.

**Un CFDI que se quedaba «Timbrando» no tenía salida.** SW Sapien devuelve el timbre de una llave repetida (`customid`) durante 72 horas desde su primer uso, y TienditApp lo pide hasta las 71 (`StampPendingInvoicesCommandHandler.RetryWindow`, ADR 0045). Pasado eso, el CFDI se quedaba «Timbrando» para siempre:

- «Volver a pedir el timbre» mandaba una solicitud que el PAC ya no reconoce, con una fecha demasiado vieja para timbrar.
- Si el PAC sí lo había timbrado, su rechazo invitaba a facturar otra vez lo mismo: dos CFDI de la misma venta.
- El rechazo tardío decía «revisa en el portal del PAC», pero la cuenta del PAC es de la plataforma (ADR 0045): el negocio no entra a ese portal.

Lo que sabemos de SW Sapien:

- `datawarehouse/v1/live/{uuid}` entrega el XML que guarda de un folio fiscal de la cuenta, y responde sin registros a uno que no tiene.
- No hay búsqueda por serie, folio ni llave sin el folio fiscal.

Lo que sí tiene el negocio es el portal del SAT. En Factura electrónica › Consultar facturas emitidas, con su e.firma o su contraseña, ve cada CFDI de su RFC, timbrado por cualquier PAC, con su folio fiscal, fecha, receptor y total.

Tomé las decisiones de la Fase 28K con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **El apartado se renueva mientras se trabaja** (`LeaseKeeper<TItem>`, Infrastructure). El outbox, el inbox del webhook y la cola de mensajes lo usan igual.
   - Cada tercio del apartado alarga `LockedUntil` de cada elemento del lote que no ha terminado, también los que esperan su turno. Así fallan dos renovaciones antes de que venza.
   - La renovación conserva el `LockToken` con el que esta instancia lo tomó, y el `UPDATE` siempre lo lleva en su `WHERE`, aunque `LockedUntil` no cambie. Si otra instancia lo tomó (esta se detuvo más que el apartado), su token ya es otro: la renovación no encuentra la fila y el elemento se pierde. Se cancela el token con el que trabaja, y esta instancia no lo completa ni lo marca fallido. Los handlers del outbox que ya terminaron quedan registrados (`OutboxMessageConsumers`), y la otra instancia los salta.
   - Completar compara el mismo token, así que una renovación nunca invalida lo que el handler ya terminó.
   - Renovar y completar nunca se cruzan: cada uno adjunta el elemento a su propio contexto, uno a la vez.
   - Una renovación que falla (la base no responde) se registra, el elemento vuelve a lo que guarda la base y se intenta en la siguiente vuelta, dentro de lo que queda del apartado.
   - No cambia el esquema ni la configuración.
2. **Mientras un CFDI está «Timbrando», su DTO dice hasta cuándo TienditApp pide su timbre** (`StampWindowEndsAt` = `RequestedAt` + 71 h). Vale para la factura de un pedido, la global, el complemento de pago y la nota de crédito, en sus DTO y en el de CFDI emitidos, que dice además desde cuándo se puede decir que no se timbró (`NotStampedFrom`, decisión 6).
3. **Pasada esa ventana, ya no se pide otra vez.**
   - «Volver a pedir el timbre» se rechaza con `StuckStamps.PastWindowMessage`, en los cuatro tipos.
   - El cliente ya no lo ofrece. Dice en su lugar que se revisa en el portal del SAT y, a dueños y administradores, dónde decirlo; al resto del equipo, quién lo revisa.
   - Ese CFDI ya no se vuelve a leer cada 30 segundos en Pedidos ni en la factura global.
4. **Reportes › CFDI emitidos › Por revisar en el portal del SAT** (`GET issued-cfdis/to-review`):
   - lista todo CFDI del negocio que sigue «Timbrando» pasada la ventana, de cualquier mes y tipo, el más viejo primero, hasta 100;
   - de cada uno muestra lo que lo encuentra en el portal: su fecha, su receptor y su total;
   - ofrece «No se timbró» desde `NotStampedFrom`; antes, la fila dice desde cuándo;
   - solo aparece cuando hay alguno; al resolver el último, el foco va a su título, que dice que ya no queda ninguno;
   - la lista y el resumen del mes ya no se vuelven a leer cada 30 segundos por los que se quedaron timbrando.
5. **«Ya se timbró»** (`POST issued-cfdis/{kind}/{cfdiId}/stamped`) recibe el folio fiscal que muestra el portal del SAT.
   - `IInvoicingGateway.FindStampedAsync` trae del PAC el XML de ese folio fiscal: `null` si la cuenta de la plataforma no lo tiene, un error temporal si el XML es de otro folio, y un rechazo si SW Sapien lo tiene cancelado ante el SAT (un CFDI cancelado no puede ser el timbre de uno que sigue «Timbrando»).
   - `StuckStamps.Mismatch` revisa que el XML sea de este CFDI: serie, folio, fecha (la del comprobante, idéntica en cada intento), tipo de comprobante, total, RFC del emisor y RFC del receptor. Si no, nada cambia y el mensaje lo dice.
   - Se guarda como cuando el PAC responde (`OrderInvoiceStamper.KeepStampAsync`): el XML, el timbre, el correo al cliente (si lo había) y el PDF.
   - Una sustituta (ADR 0051) timbrada así pide de inmediato la cancelación con motivo 01 de la factura que sustituye, como «Volver a pedir el timbre» cuando la timbra: el worker ya no la busca pasadas 72 horas. Si el SAT no la toma, la sustituida guarda el motivo y «Cancelar con relación» la vuelve a pedir.
   - Queda en la auditoría (`CfdiStampConfirmed`) quién lo dijo y con qué folio fiscal.
6. **«No se timbró»** (`POST issued-cfdis/{kind}/{cfdiId}/not-stamped`) lo deja «Rechazada», con el motivo `StuckStamps.NotStampedReason` («No se timbró: se revisó en el portal del SAT.»), y lo que cubría se puede facturar otra vez. Una sustituta rechazada así le devuelve al pedido la factura que iba a sustituir, como cuando el PAC la rechaza (ADR 0051).
   - Solo vale desde `NotStampedFrom` = `RequestedAt` + 71 h + 72 h. El SAT tarda hasta 72 horas en mostrar un CFDI timbrado, y el PAC pudo timbrarlo en el último intento: antes de eso, no encontrarlo en el portal no dice nada (`SatMayNotShowItYetMessage`). «Ya se timbró» vale desde que cierra la ventana.
   - Quién lo revisó va a la auditoría (`CfdiNotStampedConfirmed`), no al motivo que lee todo el equipo.
7. **Las dos solo valen pasada la ventana y mientras sigue «Timbrando»** (`InWindowMessage`, `NotStuckMessage`). La versión de la fila impide que dos personas, o una persona y el PAC, escriban encima una de la otra: la que llega después recibe `NotStuckMessage` y lo que guardó la primera se queda. El worker ya no los toca.
8. **Inicio avisa** a dueños y administradores con `cfdisToReview` («N CFDI por revisar en el portal del SAT»), como urgente: nada lo resuelve solo.
9. **Acceso:** dueños y administradores (`RequiresTenantManager`), como Reportes › CFDI emitidos. Todo lee con el filtro del negocio, y la suite entre negocios cubre las tres rutas.
10. **Los textos que nombraban el portal del PAC** ahora nombran el del SAT, también el del rechazo tardío (`OrderInvoiceStamper.LateRefusalNote`).
11. **Sin migración.** Los índices filtrados `(Status, RequestedAt)` de las cuatro tablas, para los que esperan algo, ya acotan la lista y el aviso.
12. **La API se mantiene compatible:**
    - tres rutas nuevas;
    - un campo nuevo, opcional, en cinco DTO (`StampWindowEndsAt`), y otro en el de CFDI emitidos (`NotStampedFrom`);
    - una llave nueva de Inicio;
    - cambia de comportamiento: volver a pedir el timbre pasada la ventana se rechaza (antes mandaba una solicitud que el PAC ya no podía timbrar).
13. **Fuera de esta fase:**
    - consultar al SAT de forma automática (su descarga masiva pide la e.firma del negocio);
    - buscar en el PAC sin el folio fiscal;
    - un aviso por correo o por WhatsApp de los CFDI por revisar.

## Consecuencias

- **Lo que logra:**
  - El correo de un CFDI o un mensaje de WhatsApp lento ya no se manda dos veces porque venció su apartado.
  - Un CFDI que se quedó «Timbrando» sale de ahí: con su timbre si el PAC lo hizo, o rechazado si no, y nunca con una segunda solicitud que pueda duplicarlo.
- **Lo que limita:**
  - La entrega de las colas sigue siendo al menos una vez. Si esta instancia se detiene más que el apartado entero (una pausa larga o la base caída más de dos tercios del apartado), otra puede repetir un handler que ya mandó algo. Los handlers siguen siendo idempotentes donde pueden (ADR 0006).
  - La revisión es de una persona. Si dice «No se timbró» de uno que el PAC sí timbró (lo buscó mal, o el SAT tardó más de lo que dice), habrá dos CFDI de la misma venta, y uno se cancela en el portal del SAT. Por eso «No se timbró» espera las 72 horas en que el SAT muestra lo timbrado, y la auditoría dice quién lo revisó.
  - «Ya se timbró» depende de que SW Sapien guarde el XML de ese folio fiscal. Si todavía no lo entrega, el mensaje pide intentarlo en unos minutos.
- **Compatibilidad durante el despliegue:**
  - El cliente anterior no lee los campos nuevos y sigue ofreciendo «Volver a pedir el timbre»; ahora la API la rechaza pasada la ventana, con el mensaje que dice dónde revisarlo.
  - El cliente nuevo con la API anterior no recibe la ventana: ofrece «Volver a pedir el timbre» como antes, y la sección por revisar falla con su aviso.
- **Reversibilidad:** sin cambios en la base; volver a la versión anterior solo devuelve el comportamiento de antes.
