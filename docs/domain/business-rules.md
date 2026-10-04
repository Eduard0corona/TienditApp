# Reglas de negocio

## Órdenes

- Una orden nunca refiere a la vez a `CustomerUserId` y `TemporaryCustomerId`. La orden de un cliente exige uno de los dos; una venta de mostrador creada por el negocio (`CreatedByBusiness`) a público en general puede no tener ninguno. `CK_Orders_CustomerLink` aplica la misma regla en la base de datos.
- Una orden inicia en `Pending` y su estado cambia mediante `TransitionTo` o `Cancel` con una máquina creada por tipo de negocio/orden.
- Solo se agregan items mientras la orden está `Pending`; cantidad debe ser positiva y precio no negativo.
- Las órdenes Service pueden tener `ServiceOrderDetail` y piezas; otros tipos no.
- Las colecciones de items, historial, pagos y piezas son de solo lectura.
- Un pago debe ser positivo, no puede exceder `Total` acumulado y no se admite en una orden cancelada. Una venta puede crearse con sus pagos (hasta 10); si uno es inválido, no se crea.
- El IVA de cada línea se fija al venderla (tasa del producto o del negocio) y la orden conserva si sus precios incluían IVA; cambiar la política después no altera ventas anteriores.
- Una venta de mostrador pagada se cierra sola si el negocio mantiene `AutoCloseCounterSales` (ver [ciclos de vida](order-lifecycles.md)).
- Una orden con pagos no se puede cancelar mientras no existan reembolsos; cancelar cancela también sus piezas aceptadas o en progreso.
- Una orden admite como máximo 200 líneas, 10,000 unidades por línea y 500 piezas de servicio, además del `MaxQuantityPerOrder` de cada producto.
- La creación enlaza la cuenta cuyo perfil tiene ese teléfono solo si esa cuenta lo confirmó por SMS (`PhoneNumberConfirmed` y su teléfono confirmado es el del perfil, `CustomerPhoneOwnershipRules`); si no, crea/usa cliente temporal. El teléfono es opcional solo en ventas de mostrador a público en general (sin cliente seleccionado o con el cliente "Publico en general"): la orden queda sin cliente, con nombre "Público en general" si no se captura otro, y no recibe avisos de WhatsApp. Un cliente con nombre y los pedidos del propio cliente siguen requiriéndolo. Un teléfono que no se puede normalizar a E.164 responde 400 con un mensaje en español (`PhoneNumberRules.InvalidPhoneMessage`) en lugar de un 500.
- Las órdenes de un cliente temporal se vinculan a una cuenta solo cuando su dueño confirma el teléfono (`ConfirmPhoneCommand`), en la misma transacción que la confirmación; el registro de cliente no las vincula.
- Registrar un teléfono no lo aparta: si lo tiene un perfil no verificado, la cuenta nueva nace sin perfil. Al confirmar el teléfono, el perfil de quien lo confirmó toma el número (se crea o cambia de número) y el perfil no verificado lo pierde; su cuenta se conserva. Si otra cuenta ya lo confirmó, el registro se rechaza y una confirmación posterior no le quita el número.

## Cancelación y trazabilidad

El agregado conserva `CancellationReason`, agrega historial con estado anterior/nuevo/usuario y emite evento con estado anterior/motivo. Sin embargo, el motivo es opcional y `OrderStatusHistory` no guarda el motivo; es una brecha frente a la regla aprobada de conservar motivo, estado anterior, usuario y trazabilidad completa. Toda cancelación futura debe exigir/normalizar la evidencia necesaria sin reescribir historial.

## Servicio y citas

- Una pieza inicia `Accepted`; pasa a `InProgress`, luego `Completed`, o puede cancelarse desde los dos primeros estados.
- Una pieza completada/cancelada no puede asignarse.
- El caso de uso sincroniza estado de la orden desde las piezas trackeables. Los cambios de pieza pasan por `Order.ChangeServicePieceStatus` para que el rowversion de la orden detecte cambios simultáneos.
- Completar una pieza consume su receta en el mismo guardado; si falta stock, la pieza no se completa.
- Las citas tienen su propio enum y método de cambio de estado; deben mantener reglas en el agregado.

## Inventario, compras y caja

- Existencia disponible es `OnHandQuantity - ReservedQuantity`; una salida normal no permite negativo.
- Reservado no puede ser negativo ni superar existencia.
- Los movimientos tienen líneas positivas y el tipo define el signo aplicado.
- Una compra no repite insumo+ubicación, no recibe más de lo pendiente y distingue recepción parcial/total.
- Una caja cerrada no recibe movimientos y no puede cerrarse dos veces; el monto contado al cerrar no puede ser negativo.
- El tipo de movimiento de caja define su signo; los importes capturados son positivos. Solo el efectivo forma parte del saldo esperado.
- Solo puede haber una caja abierta por tenant.
- Solo las entradas modifican el costo promedio de un insumo.

## Guardrails

- No cambies estados por asignación directa.
- No calcules estados/totales/impuestos/saldos/existencias finales en frontend.
- Cambios que cruzan orden, inventario, caja, auditoría o notificación pasan por casos de uso coordinados.
- Mantén invariantes en agregados y formato/entrada en FluentValidation/Application.
- Toda nueva regla necesita pruebas positivas y negativas.
