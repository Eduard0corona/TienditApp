# ADR 0079: Cuánto tarda una cocina en tener listo el pedido en línea

- Estado: Aceptada en la Fase 29Y (2026-11-08).
- Fecha: 2026-11-08
- Continúa: [ADR 0074](0074-order-confirm-time.md), «Fuera de esta fase»: medir cuánto tarda en preparar o entregar el pedido.

## Contexto

Desde la Fase 29T, Explorar dice en cuánto suele confirmar un negocio sus pedidos en línea ([ADR 0074](0074-order-confirm-time.md)). Pero confirmar es el primer paso: el cliente de una cocina espera, sobre todo, a que su pedido esté listo para pasar por él o para que salga a entregarse.

El pedido ya guarda en su historial cada cambio de estado con su hora (`OrderStatusHistory`), también cuando se marca «Listo». Solo una cocina tiene ese paso: los pedidos de un negocio de comida siguen su propia máquina de estados, y los de una tienda o un oficio no pasan por «Listo». No hace falta guardar nada nuevo.

Medir la entrega no se puede con lo que hay: un pedido a domicilio pasa de «Listo» a «Entregado» sin un paso de «salió a entregar», y la mayoría de los negocios marcan «Entregado» cuando ya lo cobraron, no cuando llegó.

El dueño del producto pidió avanzar este punto (el 5), entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29Y con esa autorización. No lleva migración.

## Decisión

1. **Qué cuenta:**
   - Los mismos pedidos que el tiempo de confirmar ([ADR 0074](0074-order-confirm-time.md)): los que sus clientes hicieron en su menú en línea en los últimos 90 días, sin los de una mesa ni los que tomó el equipo.
   - **Solo los que se marcaron «Listo»** alguna vez. Uno que el negocio entregó sin marcarlo listo, uno cancelado antes y uno que todavía no está listo no cuentan, ni como rápidos ni como lentos.
   - **La espera** va desde que llegó el pedido hasta que se marcó «Listo»: con el tiempo de confirmarlo, porque es lo que espera su cliente. Un pedido llega a «Listo» una sola vez: su máquina de estados solo sale de ahí para entregarlo.
   - **Cada cliente cuenta una vez**, por el último de sus pedidos que se marcó listo, como las solicitudes ([ADR 0067](0067-requests-that-count.md)). El teléfono y quién lo marcó solo se comparan en la base, nunca se leen.
2. **Qué dice:** desde 3 pedidos, el tramo más corto que reúne al menos la mitad (`TypicalWait`, como responder, cotizar y confirmar). «Menos de» es estricto:
   - «Suele tener tu pedido listo en menos de 15 minutos»;
   - «… en menos de media hora»;
   - «… en menos de una hora».
   - Con una hora o más, o con menos de 3 pedidos, no dice nada.
3. **Dónde:**
   - En la tarjeta de Explorar de una cocina que toma pedidos en línea, aunque estén en pausa, junto a su tiempo de confirmar.
   - En su menú, salvo en una mesa.
   - Para su dueño, en «Cómo te ven en TienditApp», solo si es una cocina. Ahí también lee si tarda una hora o más, o que todavía no se cuenta y cuándo cuenta un pedido: «uno cuenta cuando lo marcas como «Listo» en Pedidos o en Cocina».
4. **De dónde:** de la foto de Explorar, como el tiempo de confirmar.
   - Una sola lectura entre negocios, filtrada por las cocinas listadas (los pedidos de los demás negocios ni se leen), une cada pedido con su fila de «Listo» en el historial, dentro del mismo negocio.
   - La misma lectura agrupa los pedidos de cada teléfono y dice, de cada tramo, cuándo llegó el último ([ADR 0077](0077-explore-order-with-every-time.md)). Solo vuelven el negocio y esas horas.
   - Si no se puede leer, nadie lo dice hasta la siguiente foto, y al dueño no se le dice que todavía no se cuenta. Los demás tiempos siguen, y una lectura fallida del tiempo de confirmar no calla este: cada uno depende de su propia lectura.
5. **No cambia el orden de Explorar:** una cocina sigue por su tiempo de confirmar ([ADR 0077](0077-explore-order-with-every-time.md)).
6. **Fuera de esta fase:**
   - ordenar Explorar por el tiempo de tener listo el pedido;
   - medir la entrega: pide guardar cuándo sale el pedido a entregarse;
   - decirle al cliente, en el seguimiento de su pedido, para cuándo suele estar listo;
   - avisarle a la cocina cuando un pedido lleva tiempo en preparación.

## Consecuencias

- **Lo que logra:**
  - El cliente de una cocina sabe, antes de pedir, en cuánto suele estar listo su pedido, no solo en cuánto se confirma.
  - La cocina tiene una razón para marcar «Listo» en Pedidos o en Cocina.
  - Aparece desde el despliegue: el historial ya guarda los pedidos de los últimos 90 días.
- **Lo que limita:**
  - **Depende de que la cocina marque «Listo»:** una que entrega sin marcarlo no dice nada, y una que lo marca tarde (al entregarlo) se ve más lenta de lo que es.
  - **No distingue lo que se pidió:** un pedido grande y uno de una bebida cuentan igual. Es lo que el cliente lee: lo usual de esa cocina.
  - **Se puede inflar** con pedidos falsos desde varios teléfonos, como los demás tiempos ([ADR 0064](0064-response-time-in-explore.md)). Es una señal para el cliente, no una garantía.
  - **Costo de la foto:** cada foto (cada 30 segundos, si alguien la pide) lee otra vez los pedidos en línea de 90 días de las cocinas listadas, como el tiempo de confirmar.
    - Une cada uno con su fila de «Listo» en una sola lectura. Medida aparte, con una sola cocina en SQL Server: unos 85 ms con 25,000 pedidos (2,500 en línea) y entre 450 y 550 ms con 100,000 (10,000 en línea).
    - La forma con una subconsulta en cada tramo tardaba unos 140 y entre 700 y 1,000 ms, y la que buscaba el primer «Listo» de cada pedido con un APPLY, de 12 a 30 segundos con un plan que SQL Server elegía.
    - Un pedido llega a «Listo» una sola vez: su máquina de estados solo sale de ahí para entregarlo. Si alguna vez llegara dos veces, contaría en los dos tramos y su cliente, en el más lento.
    - Comparte el límite del tiempo de confirmar: con varios negocios de ese tamaño conviene el índice filtrado de los pedidos en línea, que pide migración.
  - **Contrato:** la tarjeta de Explorar y el menú suman `readyTime`, y «Cómo te ven», `countsReady` y `readyTime`. Los clientes anteriores los ignoran.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - qué cuenta (desde que llega, sin los que nunca se marcaron listos);
  - los tramos (15, 30 y 60 minutos);
  - que no cambie el orden;
  - los textos.
- **Reversibilidad:** sin migración. Volver al código anterior quita el tiempo de tener listo el pedido.
