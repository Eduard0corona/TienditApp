# ADR 0017: Zonas de entrega por código postal

- Estado: Aceptada
- Fecha: 2026-10-13

## Contexto

Los clientes reales de TienditApp son negocios de comida a domicilio ([ADR 0016](0016-delivery-only-branches.md)). La pregunta que más reciben por WhatsApp es «¿llegan a mi colonia y cuánto cuesta el envío?». Hasta hoy, TienditApp no sabía nada de eso: ni a qué códigos postales entrega un negocio, ni cuánto cobra, ni desde qué monto toma un pedido. Cada negocio lo contestaba a mano. La Fase 22C cobrará el envío en el pedido a domicilio, y para eso primero hace falta saber a dónde entrega y a qué precio.

## Decisión

1. **El negocio tiene zonas de entrega** (`DeliveryZone`, agregado con su negocio, `ITenantScoped`). Cada zona lleva:
   - nombre (hasta 60 caracteres), uno por zona en el negocio;
   - códigos postales, de 1 a 300, de cinco dígitos y sin repetir;
   - costo de envío, de $0 a $10,000;
   - pedido mínimo, de $0 a $100,000; $0 es sin mínimo;
   - «envío gratis desde», opcional; no aplica si el envío ya es gratis;
   - si está activa.

   Un negocio tiene hasta 30 zonas. No hay polígonos ni mapas: el código postal es lo que el cliente sabe y escribe.
2. **Un nombre y un código postal están en una sola zona del negocio.** Lo garantiza la base con dos índices únicos, `(TenantId, Name)` de `DeliveryZones` (sin distinguir mayúsculas) y `(TenantId, PostalCode)` de `DeliveryZonePostalCodes`, aunque dos guardados los agreguen al mismo tiempo. El caso de uso dice qué zona lo tiene: «El código postal 72005 ya está en la zona «Centro». Quítalo de ahí primero.» o «Ya tienes una zona llamada «Centro». Ponle otro nombre.». Así un mensaje que nombra una zona apunta a una sola. Otro negocio puede tener el mismo nombre y el mismo código postal.
3. **Los montos son lo que paga el cliente, con IVA incluido.** El pedido mínimo y el envío gratis se comparan con el total de productos tal como lo ve el cliente, sin el envío. `FeeFor` y `MeetsMinimum` quedan en el dominio para la 22C. Cuando el envío entre al pedido, su base gravable saldrá de ese monto con la tasa del negocio, y se registrará en el CFDI como concepto propio; se decidirá en la 22C.
4. **Cada cambio queda registrado y nadie pisa el de otro.**
   - Crear, guardar y borrar zonas es para dueños y administradores (`[RequiresTenantManager]`). Leerlas es para todo el equipo, porque quien toma un pedido necesita saber a dónde se entrega.
   - Guardar o borrar lleva la revisión que vio la persona: si alguien guardó o borró antes, la respuesta es 409 `concurrency_conflict`. También cuando la base lo dice con el índice único o la llave, porque escribió primero el código postal nuevo: el caso de uso lee otra vez la zona para distinguirlo. Guardar sin cambios no registra nada.
   - La bitácora (`DeliveryZoneCreated`, `DeliveryZoneUpdated`, `DeliveryZoneDeleted`) guarda lo que cobra la zona y cuántos códigos postales tiene, antes y después. No guarda cuáles: una zona puede tener cientos.
5. **Las respuestas automáticas contestan la cobertura.**
   - Cuándo aplica: el negocio tiene al menos una zona activa y el mensaje:
     - pregunta por la entrega sin dudas («envíos», «a domicilio», «reparten», «código postal», «CP», «zonas», «hacen envío», «envían a», «entregan en», «llegan a», «cuánto cuesta el envío»…);
     - usa una palabra que también dice otra cosa («envío», «envían», «mandan», «entregan», «entregas», «llegan», «zona», «colonia») junto a un código postal: «¿Envían al 72000?» sí, «Te envío el comprobante» no;
     - o trae solo un código postal («72000», «CP 72000», «mi código postal es el 72000»).
   - «¿Cuánto cuesta el envío?» pregunta por la entrega, no por los precios: no agrega los productos, salvo que también pregunten por ellos.
   - Si llegan: «Sí llegamos al 72000: el envío cuesta $35.00. Pedido mínimo de $150.00. Envío gratis en pedidos desde $400.00.» Cada frase aparece solo si aplica.
   - Si no llegan: «Por ahora no llegamos al 72000.»
   - Sin código postal: «Sí hacemos entregas a domicilio. Escríbenos tu código postal y te decimos si llegamos y cuánto cuesta el envío.»
   - Una zona desactivada no llega a ninguno de sus códigos postales.
   - El nombre de la zona no sale en la respuesta, porque puede ser un nombre interno.
   - La cobertura se arma con el resto, igual que el horario: la bienvenida va antes y las preguntas de datos al final si saludan; «solo a domicilio» va antes si preguntan dónde están; los productos van después si preguntan precios; y al final va el enlace al menú.
   - Sin zonas activas, o si no se pueden leer (queda en el log, sin teléfono, texto ni código postal), todo sigue como antes.
6. **Todo sigue llegando a la sucursal principal.** Las zonas son del negocio, no de una sucursal. Enrutar por código postal a otra cocina queda para cuando haga falta.

## Consecuencias

- **Lo que logra:**
  - El negocio deja de contestar a mano la pregunta más frecuente.
  - El equipo ve en un solo lugar, Configuración › Zonas de entrega, a dónde entrega y a qué precio, y puede probar un código postal.
  - La 22C tiene ya la regla del envío y del mínimo.
- **Lo que limita:**
  - El menú en línea todavía no ofrece «A domicilio» ni cobra el envío: lo hace la 22C, junto con el verificador de código postal del menú. Mostrar un costo que el pedido no cobra confundiría al cliente.
  - No hay horario por zona ni tiempo estimado de entrega.
  - Un código postal grande puede cubrir colonias a las que el negocio no llega. La zona entrega a todo el código postal o a nada.
  - Una pregunta con un número de cinco dígitos que no es un código postal («envío de un pedido de 10000») recibe la respuesta de un código postal. Es raro y no publica nada privado.
  - «¿Envían a Coyoacán?» recibe la petición del código postal, pero «¿Me envían la factura?» no recibe nada de entregas, y tampoco «¿Envían?» sin más: es el precio de no contestar de entregas a quien manda un comprobante.
- **Decisión por omisión:** se tomó sin una preferencia expresa del dueño del producto, con su autorización para decidir. Si se prefiere que el costo sea antes de IVA, el cambio está en la 22C, al cobrarlo; las zonas guardan el número que escribe el negocio.
- **Reversibilidad:**
  - Volver a la versión anterior de la app no necesita la base: no conoce las tablas, y las zonas se quedan sin usarse.
  - El `rollback.sql` se detiene con `THROW 50012` mientras haya zonas guardadas. Sin zonas, quita las dos tablas, y correrlo otra vez no hace nada.
