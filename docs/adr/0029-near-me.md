# ADR 0029: «Cerca de mí»: la ubicación de cada sucursal y Explorar por distancia

- Estado: Aceptada
- Fecha: 2026-10-18

## Contexto

Explorar (ADR 0021) ordena los negocios por si reciben pedidos ahora y por nombre. Dice la ciudad de cada uno, pero no qué tan lejos está del cliente. Las apps de entrega abren con lo más cercano. Hoy, quien busca dónde comer cerca, o un lugar al que ir a recoger, lee la lista completa de la ciudad.

TienditApp no sabe dónde está cada sucursal: guarda su dirección como texto, sin un punto en el mapa. Por eso «Cómo llegar» busca la dirección en Google Maps, y una dirección mal escrita lleva a otro lado.

Faltaban tres decisiones: de dónde sale el punto de cada sucursal, de dónde sale la ubicación del cliente y qué tan precisa se guarda y se muestra cada una.

## Decisión

1. **Cada sucursal puede tener su punto en el mapa.**
   - **Qué se guarda:** latitud y longitud en grados, como las escribe Google Maps (`GeoPoint`, seis decimales, unos 11 cm). Las dos o ninguna, en rango; la base lo garantiza con `CK_Branches_Location`. Un punto 0, 0 no se acepta: es lo que escribe un dispositivo que no sabe dónde está.
   - **De dónde sale (Configuración › Sucursales):** sin mapa dentro de la app, sin servicio de geocodificación y sin llaves:
     - **Pegado de Google Maps:** el dueño mantiene presionado su local y copia las coordenadas que aparecen. También sirve el enlace de un lugar, que trae el punto dentro; un enlace corto no lo trae, y el campo lo dice.
     - **Desde el dispositivo:** «Usar mi ubicación» la toma del dispositivo, para quien está en la sucursal.
     - **Para comprobarlo:** «Revisar en el mapa» abre en Google Maps lo escrito, antes de guardar.
   - **Cómo se guarda:**
     - Una pantalla que no conoce el campo (una pestaña abierta antes del despliegue) no lo manda, y la sucursal conserva su punto.
     - Vaciarlo quita la sucursal del mapa.
   - **Quién:** el dueño o un administrador, como el resto de la sucursal (`[RequiresTenantManager]`).
2. **Explorar mide la distancia a la sucursal principal, y solo si recibe clientes.**
   - **Por qué la principal:** es la misma cuya dirección y ciudad muestran el menú y la tarjeta, y a la que llegan los pedidos en línea. Medir a otra sucursal llevaría a recoger el pedido a un lugar distinto del que dice el menú.
   - **Una sucursal solo a domicilio nunca se mide** (ADR 0016): su dirección es del equipo. Su punto se guarda si el dueño lo pone, pero ninguna respuesta pública lo lleva, ni siquiera como distancia: varias distancias desde lugares distintos dirían dónde está. Si la sucursal abre su local, su punto vuelve a contar.
   - **Las otras sucursales** pueden tener su punto, para el equipo. Hoy no cambian nada para el cliente.
3. **La ubicación del cliente sale de su navegador, solo cuando toca «Cerca de mí».**
   - **Cuándo se pide:**
     - El chip «Cerca de mí» de Explorar la pide al navegador, que pregunta al cliente la primera vez.
     - Queda en la dirección (`?cerca=1`), así que una búsqueda compartida o un regreso con el botón de atrás la vuelven a pedir.
     - Si el navegador no la da, el chip dice qué hacer y ofrece «Intentar de nuevo».
   - **Qué viaja:** solo a la búsqueda de Explorar (`latitude` y `longitude` en `GET /api/v1/public/directory`), redondeada a tres decimales, unos 110 m. El servidor también la lee así, por si otro cliente manda más.
   - **Qué se guarda:** nada.
     - El servidor no la guarda ni la escribe en un mensaje de error; el rastreo oculta los valores de la URL.
     - El cliente la tiene en memoria mientras la página está abierta, nunca en el dispositivo.
   - **Sin «Cerca de mí»,** Explorar se ve y se ordena como antes.
4. **Con «Cerca de mí», Explorar ordena por distancia.**
   - **El orden:**
     1. primero los abiertos ahora (los que reciben pedidos, agendan en línea o muestran su menú abiertos), del más cercano al más lejano;
     2. luego los cerrados, igual;
     3. en cada grupo, los que no están en el mapa van al final, en el orden de Explorar.
   - **La distancia:** cada tarjeta dice qué tan lejos está («a 300 m», «a 2.5 km», «a 12 km»). Es en línea recta (haversine), no el camino por las calles. El servidor la manda en kilómetros con un decimal.
   - **Los productos de una búsqueda:** siguen el mismo orden, después de los que tienen las palabras en el nombre, y cada uno dice la distancia de su negocio.
   - **Las demás reglas no cambian:** la categoría, la búsqueda, el código postal (si entrega ahí y cuánto cobra) y las páginas siguen igual. «Tus favoritos» no lleva distancia.
5. **La tienda abre su punto exacto.**
   - **«Cómo llegar»:** en la información de la tienda y al recoger, abre en Google Maps el punto de su sucursal principal si recibe clientes y está en el mapa; si no, busca su dirección, como antes.
   - **Para los buscadores:** sus datos estructurados (ADR 0027) llevan el punto como `geo` (`GeoCoordinates`).
   - **Una sucursal solo a domicilio no lo lleva,** igual que su dirección.

## Consecuencias

- **Lo que logra:**
  - Un cliente ve primero lo que tiene cerca y sabe qué tan lejos está cada negocio.
  - «Cómo llegar» lleva al local y no a donde Google entienda la dirección.
  - Los buscadores saben dónde está cada tienda.
  - Nada de esto pide un servicio de pago, una llave ni un permiso nuevo de la app.
- **Lo que limita:**
  - **Las distancias:**
    - son en línea recta, no por calles ni en tiempo;
    - con unos 110 m de error por el redondeo, una distancia corta («a 300 m») es aproximada;
    - no hay radio: con «Cerca de mí», un negocio a 500 km sale al final, pero sale.
  - **Sin mapa dentro de la app:** el dueño comprueba su punto en Google Maps, fuera de la app, y un punto mal pegado se ve hasta que lo revisa.
  - **Sin ubicación del navegador,** no hay «Cerca de mí»: no se calcula desde el código postal (no hay un catálogo de coordenadas por código postal con licencia clara).
  - **Lo que todavía busca la dirección como texto:** la tarjeta de una cita en Mis citas, la ruta de servicios a domicilio y la de los repartidores.
  - **Las sucursales:** la distancia es a la principal; un negocio con varias sucursales con local no aparece cerca por las otras.
- **Privacidad:**
  - La ubicación del cliente pasa por la URL de una petición. La app no la registra, y el rastreo (OpenTelemetry) oculta los valores de la URL.
  - Un registro de accesos del servidor web que guarde la URL completa (los registros HTTP de App Service, si se encienden) la guardaría con unos 110 m de precisión, como hoy guarda el código postal. Es preferible dejarlos apagados o sin la consulta.
- **Reversibilidad:**
  - **Hay una migración** (`AddBranchLocation`: dos columnas con nulos en `Branches` y su restricción) con scripts idempotentes en `database/production/`.
  - **Su `Down` se detiene** (error 50022) en cuanto una sucursal está en el mapa: revertir olvidaría dónde está. Se corrige hacia adelante.
  - **Volver al código anterior** funciona sin tocar la base: no conoce las columnas, guarda las sucursales como siempre y Explorar deja de ordenar por distancia.
