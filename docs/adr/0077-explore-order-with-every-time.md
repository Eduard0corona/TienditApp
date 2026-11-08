# ADR 0077: El orden de Explorar con todos sus tiempos y la visita que nunca se cotizó

- Estado: Aceptada en la Fase 29W (2026-11-08).
- Fecha: 2026-11-08
- Continúa: [ADR 0073](0073-quote-time-and-explore-order.md), «Fuera de esta fase»: contar como lenta una visita que nunca se cotizó y ordenar por el tiempo de cotizar; y [ADR 0074](0074-order-confirm-time.md), «Fuera de esta fase»: ordenar Explorar por el tiempo de confirmar.

## Contexto

Explorar dice tres tiempos: en cuánto suele responder un negocio sus solicitudes en línea ([ADR 0064](0064-response-time-in-explore.md)), en cuánto suele mandar un oficio la cotización después de la visita ([ADR 0073](0073-quote-time-and-explore-order.md)) y en cuánto suele confirmar un negocio sus pedidos en línea ([ADR 0074](0074-order-confirm-time.md)). Pero solo ordenaba por el primero: un restaurante, que no toma solicitudes, quedaba siempre en medio de su grupo, confirmara en minutos o en una hora, y un oficio que responde al momento quedaba primero aunque tardara semanas en cotizar.

Además, el tiempo de cotizar solo contaba las visitas cotizadas. Un oficio que visita y nunca manda la cotización no se veía lento: ni siquiera llegaba a 3 visitas para decir algo.

El dueño del producto pidió avanzar estos puntos (el 2, el 3 y el 4), entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29W con esa autorización. No lleva migración.

## Decisión

1. **Una sola escala para ordenar.** Cada tiempo pone al negocio en un lugar del 0 (primero) al 4 (al final), dentro de su grupo. Cada tiempo se mide con su propia escala, porque el cliente de cada uno vive como rápido algo distinto:

   | Lugar | Responder solicitudes | Mandar la cotización | Confirmar pedidos |
   | --- | --- | --- | --- |
   | 0 | menos de una hora | menos de un día | menos de 5 minutos |
   | 1 | menos de 3 horas | menos de 3 días | menos de 15 minutos |
   | 2 | menos de 12 horas | — | — |
   | 3 | menos de un día | menos de una semana | menos de media hora |
   | 4 | un día o más | una semana o más | media hora o más |

   El último tramo que todavía se dice va después del lugar 2, como ya pasaba con «menos de un día» al responder; el que ya no se dice, al final.
2. **Cómo se juntan:**
   - **Un tiempo que el negocio todavía no tiene** (menos de 3 clientes, o algo que no hace) no cuenta: va por los que tiene.
   - **Un oficio va por el más lento de sus dos pasos** (responder y cotizar): su cliente espera los dos.
   - **Un negocio que toma solicitudes y pedidos va por el más rápido** de los dos: son dos formas de comprarle, y el cliente usa la que le sirve.
   - **Sin ningún tiempo**, en el lugar 2, en medio, como antes.
   - Vale en cada grupo (toma pedidos, agenda en línea o recibe visitas; abierto; cerrado): un restaurante con los pedidos en pausa también va por su tiempo de confirmar. Lo siguen todas las listas que ya seguían el orden de Explorar ([ADR 0073](0073-quote-time-and-explore-order.md)): la lista completa, cada categoría y los productos y servicios de una búsqueda. «Cerca de mí» sigue por distancia.
3. **La visita que nunca se cotizó cuenta entre las más lentas.**
   - **Cuál:** una visita en línea de un cliente que el negocio aceptó, que ya pasó (terminó y no se canceló ni se marcó como no asistida) y que lleva una semana desde que terminó sin su cotización mandada. Cuenta como mandada al cumplir la semana, así que va entre las de una semana o más.
   - **Solo cuando no salió nada para su cliente:** una cotización empezada y no descartada, o ni cotización ni el trabajo cobrado.
   - **El trabajo cobrado** es una venta de la visita, no cancelada, con algo más que el servicio de la visita. Cobrar solo la visita es el paso de antes de cotizar (así funciona «se descuenta si te contratan»), así que no la saca de la cuenta. Cuando el negocio no eligió un servicio para su visita, la visita se cobra aparte con lo de su zona, y cualquier producto de la venta es algo más.
   - **No cuentan:**
     - una visita con el trabajo cobrado y sin cotización empezada: se resolvió en la visita;
     - una cuya cotización se descartó: el negocio decidió no cotizarla;
     - una solicitud que nadie aceptó: no se llevó a cabo, y ya cuenta entre las más lentas al responder ([ADR 0064](0064-response-time-in-explore.md)).
   - **Como las demás:** cuenta en los 90 días desde que cumple la semana, sin las del equipo, y cada cliente cuenta una vez ([ADR 0067](0067-requests-that-count.md)): por la visita que se decidió al último, la de su cotización mandada o la de su semana cumplida. Si dos se decidieron al mismo tiempo, la más lenta.
   - **Si después se manda la cotización,** cuenta con su espera real, que también pasa de una semana.
4. **Lo que lee el dueño** en «Cómo te ven en TienditApp» ([ADR 0065](0065-response-time-outside-explore.md)):
   - con su tiempo: «Lo contamos desde que termina cada visita en línea hasta que mandas su cotización, en los últimos 90 días, la última de cada cliente. Una visita que lleva una semana de terminada sin su cotización mandada cuenta entre las más lentas, salvo que descartes su cotización o que cobres el trabajo en la visita sin empezar una. …»;
   - si tarda una semana o más, también: «Cuentan así también las visitas que llevan una semana de terminadas sin su cotización mandada.»;
   - si todavía no se cuenta: «… una cuenta cuando mandas su cotización, o cuando pasa una semana desde que termina la visita sin que la mandes.».
5. **De dónde:** de la misma foto de Explorar.
   - Primero lee qué negocios están listados, y compara cada visita con esa lista.
   - Una sola lectura de las visitas trae cada una con la primera versión de su cotización. Cada visita tiene a lo sumo una no descartada (lo dice un índice que ya existe, y SQL Server la encuentra con él), y una versión mandada quiere decir que la primera se mandó.
   - La misma lectura agrupa las visitas de cada teléfono y dice, de cada tramo, cuándo se decidió la última. Solo vuelven el negocio y esas horas, nunca el teléfono, y aquí se ve el tramo de la última de cada cliente.
   - El contrato no cambia.
6. **Fuera de esta fase:**
   - avisarle al oficio cuando una visita lleva días sin cotizar;
   - una acción para decir que una visita no necesita cotización;
   - el índice de las cotizaciones por fecha de envío ([ADR 0073](0073-quote-time-and-explore-order.md)), que pide migración;
   - decirle al cliente por qué un negocio va antes que otro.

## Consecuencias

- **Lo que logra:**
  - Un restaurante que confirma en minutos sube al frente de su grupo, y uno que tarda media hora o más baja al final.
  - Un oficio que responde al momento pero cotiza en una semana ya no queda primero.
  - Un oficio que visita y no cotiza ya no pasa desapercibido: su tiempo baja o deja de decirse.
- **Lo que limita:**
  - **Las escalas no son la misma espera:** minutos para pedidos, horas para solicitudes, días para cotizaciones. El orden compara lo que el cliente de cada uno vive como rápido o lento, no el tiempo.
  - **Juntar es una regla fija:** un salón que confirma en minutos sus pedidos de productos y tarda un día en responder sus citas va primero, aunque su cliente de citas espere.
  - **Hay formas de sacar una visita de la cuenta:**
    - descartar su cotización. El costo es real: su cliente no la recibe;
    - venderle algo más que la visita sin empezar la cotización;
    - en un oficio sin servicio de visita, cualquier venta de la visita la saca.
  - **Una visita que no necesitaba cotización** (se resolvió sin cobrar, o el cliente no la quiso) cuenta entre las más lentas. Solo sale si el negocio empieza su cotización y la descarta, o si la marca como no asistida.
  - **Una cotización hecha desde Cotizaciones sin elegir la visita** no es de la visita: esa visita cuenta como nunca cotizada.
  - **Al desplegar, cuentan de golpe** las visitas aceptadas de los últimos 90 días que llevan una semana sin cotización: un oficio puede bajar en Explorar, o dejar de decir su tiempo, desde el despliegue.
  - **Una visita que el equipo movió** se mide desde su nueva hora, como la espera de la cotización.
  - **Costo de la foto** (cada 30 segundos):
    - lee las visitas en línea que ya pasaron de los negocios listados, como antes, pero ahora todas y no solo las cotizadas: cada una con su primera cotización y, la que no la tiene, si tiene alguna cotización y su venta;
    - medido con un oficio, unos 230 ms con 20,000 visitas y entre 650 y 850 ms con 80,000 (la mitad cotizadas), contra 190 y entre 400 y 550 ms de la consulta anterior, que solo leía las cotizadas;
    - la forma con una subconsulta por cliente tardaba hasta 9 segundos con 80,000 visitas, y la que unía cada visita con la última de su cliente, hasta 20 segundos con un plan que SQL Server elegía con 20,000 o 40,000.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - la escala de cada tiempo y dónde va el que no dice nada;
  - el más lento de los pasos de un oficio y el más rápido entre solicitudes y pedidos;
  - una semana para contar una visita sin cotizar, qué no cuenta y qué es el trabajo cobrado;
  - el texto del dueño.
- **Reversibilidad:** sin migración. Volver al código anterior regresa el orden al tiempo de responder y deja de contar las visitas sin cotizar.
