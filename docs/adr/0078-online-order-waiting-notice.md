# ADR 0078: Avisar de un pedido en línea que lleva tiempo sin confirmar

- Estado: Aceptada en la Fase 29X (2026-11-08).
- Fecha: 2026-11-08
- Continúa: [ADR 0074](0074-order-confirm-time.md), «Fuera de esta fase»: avisarle al negocio cuando un pedido lleva tiempo sin confirmar. Usa los tramos de esa ADR y el orden de la [ADR 0077](0077-explore-order-with-every-time.md).

## Contexto

Desde la Fase 29T, Explorar dice en cuánto suele confirmar un negocio los pedidos de su menú en línea, y un pedido que espera media hora sin respuesta cuenta entre los más lentos ([ADR 0074](0074-order-confirm-time.md)). Desde la 29W ese tiempo también ordena Explorar ([ADR 0077](0077-explore-order-with-every-time.md)).

Pero el negocio solo se enteraba de un pedido nuevo con Pedidos abierto: suena una vez y sale un aviso arriba de la lista. En otra pantalla, la campana contaba los pedidos por confirmar sin decir cuánto llevaban, y abría Pedidos en los de hoy, sin filtrar. Un pedido de ayer seguía escondido. La fila del pedido decía la hora en que llegó, no hasta cuándo responderlo.

El dueño del producto pidió avanzar este punto (el 1), entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29X con esa autorización. No lleva migración.

## Decisión

1. **Qué pedido:** uno del menú en línea que sigue «Por confirmar», como en la [ADR 0074](0074-order-confirm-time.md): no uno de una mesa (lo atiende quien sirve la mesa) ni uno que tomó el equipo.
2. **Sus tiempos** (los de la [ADR 0074](0074-order-confirm-time.md)):
   - a los 15 minutos de llegar se avisa: lleva la mitad del tiempo antes de contar entre los más lentos;
   - a la media hora ya cuenta entre los más lentos de su tiempo para confirmar.
3. **En Pedidos:**
   - la API manda `confirmBy` en el pedido: media hora después de llegar, solo mientras sigue por confirmar;
   - la fila dice «Confírmalo antes de las 3:30 p.m. para que no cuente entre los más lentos», en la zona horaria del negocio;
   - pasada la media hora dice, en rojo, «Ya pasó la media hora: cuenta entre los más lentos en tu tiempo para confirmar». No dice «en Explorar»: un negocio que no aparece ahí, o un pedido que no cuenta (no es el último de su cliente), también lo lee;
   - lee el reloj cada minuto, así que el texto cambia con Pedidos abierto, como el de las solicitudes ([ADR 0066](0066-answer-requests-in-time.md)).
4. **En la campana y en «Requiere atención» de Inicio:**
   - «3 pedidos en línea por confirmar (2 llevan 15 minutos o más)»;
   - con alguno de 15 minutos o más se pone roja;
   - abre Pedidos en los que esperan, de cualquier día (`/orders?estado=por-confirmar`), también con Pedidos abierto; un filtro que la persona elige o «Limpiar filtros» quita el enlace, así que al recargar se quedan los suyos.
5. **En cualquier pantalla:**
   - quien confirma los pedidos lee «Un pedido en línea lleva 15 minutos o más sin confirmar. A la media hora cuenta entre los más lentos en tu tiempo para confirmar.», o cuántos llevan;
   - sale cuando uno más cumple 15 minutos, y suena si el dispositivo tiene prendido el sonido de los pedidos en línea (el de Pedidos, Fase 29P);
   - lo que ya esperaba al abrir la pantalla está en la campana, y no se avisa otra vez;
   - lee la cuenta de la campana, que la API calcula, cada minuto y también con la pestaña oculta: el aviso llega en más o menos un minuto.
6. **Fuera de esta fase:**
   - avisar por correo, WhatsApp o una notificación del navegador;
   - poner primero en Pedidos los que esperan;
   - que el negocio elija los minutos del aviso;
   - avisar de un pedido de una mesa que nadie atiende.

## Consecuencias

- **Lo que logra:**
  - El negocio se entera de un pedido que espera aunque esté en otra pantalla, antes de que cuente entre los más lentos.
  - La campana lleva a los pedidos que esperan, también a los de ayer.
  - Cada pedido dice hasta cuándo responderlo.
- **Lo que limita:**
  - **Es de buena fe:**
    - el aviso sale de la cuenta de la campana, así que si en el mismo minuto un pedido cumple 15 minutos y otro se confirma, la cuenta no sube y no se avisa (la campana sí lo cuenta);
    - sin conexión o con la sesión cerrada no se avisa;
    - con la pestaña oculta, el navegador puede espaciar las lecturas más de un minuto.
  - **Los minutos están en el texto:** «15 minutos» y «media hora» los dice el cliente. Si cambian los tramos de la [ADR 0074](0074-order-confirm-time.md), hay que cambiar los textos.
  - **El texto de la fila usa el reloj del dispositivo:** con la hora mal puesta dice algo equivocado. Un pedido que llegó cerca de la medianoche dice solo la hora, sin el día.
  - **Un pedido de mesa** sigue en la campana, pero no la pone roja ni se avisa: el número entre paréntesis cuenta solo los del menú en línea.
  - **Contrato:** `OrderDto` suma `confirmBy`; los clientes anteriores lo ignoran.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - avisar a los 15 minutos;
  - no avisar otra vez lo que ya esperaba al abrir;
  - el sonido de los pedidos en línea para el aviso;
  - los textos.
- **Reversibilidad:** sin migración. Volver al código anterior quita el aviso, el texto de la fila y el filtro de la campana.
