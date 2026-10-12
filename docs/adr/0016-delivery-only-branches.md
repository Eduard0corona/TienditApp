# ADR 0016: Sucursal solo a domicilio y su dirección privada

- Estado: Aceptada
- Fecha: 2026-10-12

## Contexto

Los clientes reales de TienditApp son negocios de comida a domicilio: cocinan y entregan con repartidores de Rappi, Uber Eats, DiDi o Paquetenvia, y muchas veces no tienen un local abierto al público. Su cocina suele ser una casa. Desde la Fase 19, y con la sucursal principal desde la Fase 20A ([ADR 0011](0011-opening-hours.md), punto 7), el menú en línea publica la dirección de la sucursal principal con un enlace «Cómo llegar». Para un negocio sin local eso es publicar la casa del dueño en una página que cualquiera abre por un enlace o un QR. Su única salida era borrar la calle de la sucursal, que también es la que ve el equipo y la que se copió al almacén.

## Decisión

1. **Cada sucursal dice cómo atiende** (`Branch.ServiceMode`, `BranchServiceMode`):
   - `Premises` (0), «Local abierto al público»: los clientes llegan a comer, a comprar o a recoger. Es lo que eran todas hasta hoy.
   - `DeliveryOnly` (1), «Solo a domicilio, sin local»: solo entrega. Su dirección es del equipo y nunca de los clientes (`Branch.KeepsAddressPrivate`).
   - Es de la sucursal, no del negocio: un negocio con un local y una cocina aparte marca solo la cocina.
2. **Una dirección privada nunca sale del equipo.**
   - El menú en línea toma la dirección con `AddressText.ForCustomers`, que no da ninguna para una sucursal solo a domicilio. Con la principal así, `PublicMenuDto.Address` es nulo y `PublicMenuDto.DeliveryOnly` es verdadero: el menú dice «Solo a domicilio: no tenemos local para visitar.» en lugar de la dirección y de «Cómo llegar».
   - Preguntar por WhatsApp dónde están («¿dónde están?», «dirección», «cómo llego») con la principal solo a domicilio responde «Somos solo a domicilio: no tenemos local para visitar.», la descripción que escribió el negocio y el enlace al menú, si está prendido. Lo que venga en el mismo mensaje va alrededor, como alrededor del horario: la bienvenida antes y las preguntas de datos al final si saludan, los productos después de la descripción si preguntan precios, y el horario antes de ese texto si preguntan por él. «Mi dirección» y «mi ubicación» no preguntan dónde están: el cliente da la suya, muchas veces para una entrega. Ninguna respuesta automática lleva la dirección de una sucursal, salvo lo que el negocio escriba en su descripción.
   - El ticket, las plantillas de WhatsApp, los QR y los correos no llevaban ninguna dirección, y siguen igual.
   - El equipo sí la ve: en Configuración › Sucursales y en el almacén de la sucursal, que la copia al crearse. Repartidores y plataformas la necesitan para recoger.
3. **Ninguna pantalla vieja la publica por error.**
   - `serviceMode` es opcional al crear y al guardar una sucursal: sin él, una nueva tiene local y una guardada conserva el que tenía. Una pestaña abierta antes del cambio nunca convierte una cocina en local con la dirección a la vista, y Sucursales solo lo manda al guardar si la persona lo cambió: una lista leída antes de que otra persona marcara la sucursal tampoco la regresa a local.
   - Un valor que no existe es 400 (`Elige cómo atiende la sucursal.`), y la base lo rechaza (`CK_Branches_ServiceMode`).
4. **El giro «Comida a domicilio» empieza sin local.**
   - El alta con ese giro (`BusinessIndustries.HomeDelivery`, comparado sin acentos ni mayúsculas) crea la sucursal principal solo a domicilio. La guía de inicio, como a todo negocio de comida, ofrece la plantilla «Comida a domicilio», y un giro con «comida» recomienda las categorías de comida.
   - Cambiar el giro después no cambia ninguna sucursal: cada una se marca en Sucursales, y Mi negocio lo recuerda al elegir ese giro.
5. **Lo cambian dueños y administradores**, como el resto de la sucursal (`[RequiresTenantManager]`). Leer las sucursales sigue abierto a todo el equipo.

## Consecuencias

- **Lo que logra:**
  - Una cocina sin local usa el menú en línea y las respuestas automáticas sin publicar su dirección (salvo la que escriba en la descripción de sus respuestas).
  - El negocio deja de borrar la calle de su sucursal para esconderla, y el equipo la conserva.
  - Los negocios con local siguen igual: todas las sucursales que ya existen quedan con local.
- **Lo que limita:**
  - Solo cambia lo que ven los clientes. Cómo se entrega (zonas, costo de envío, dirección del cliente, Paquetenvia) llega en las Fases 22B y 22C.
  - Las mesas y el horario siguen siendo del negocio, no de cada sucursal. Un negocio solo a domicilio no necesita mesas, y si las tiene, su QR sigue funcionando.
  - El giro decide la sucursal principal solo en el alta; para un negocio que ya existe se marca a mano.
- **Decisión por omisión:** se tomó sin una preferencia expresa del dueño del producto, con su autorización para decidir. Si se prefiere mostrar una zona aproximada (la colonia o la ciudad) en lugar de nada, el cambio está en `AddressText.ForCustomers` y en el menú, sin tocar la base.
- **Reversibilidad:**
  - Volver a la versión anterior de la app no necesita la base, pero esa versión trata todas las sucursales como locales: vuelve a publicar la dirección de una principal solo a domicilio. Antes de volver, cámbiala a local y borra su calle, o acepta que se publique (la consulta 3 de `verify.sql` dice cuáles son).
  - El `rollback.sql` se detiene con `THROW 50011` mientras haya una sucursal solo a domicilio, y si no, quita la columna y su restricción. Se puede ejecutar otra vez: la revisión corre con `EXEC` y solo mientras la columna existe (`COL_LENGTH`), porque SQL Server compila el lote entero y una columna ya borrada lo haría fallar.
