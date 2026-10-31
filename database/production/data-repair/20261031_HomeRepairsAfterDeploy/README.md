# Oficios que se registraron mientras se publicaba la Fase 29A

Procedimiento para mover a «Hogar y reparaciones» los oficios que se registraron entre el `apply.sql` de `20261031085333_AddHomeVisitRequests` y el despliegue de la Fase 29A. `verify.sql` es de solo lectura; `repair.sql` solo cambia la categoría en Explorar (`Tenants.DirectoryCategory`) de esos negocios.

## Qué puede pasar

`apply.sql` corre antes del merge, con la versión anterior todavía en producción. La migración mueve a la categoría 11 a los oficios cuya categoría sigue su giro. Un oficio que se registra después, pero antes del despliegue, recibe de la versión anterior la categoría de antes: Servicios para los cinco oficios de antes, la de su tipo para los nuevos.

Con la versión nueva, la categoría sigue al giro cuando es la que el giro sugiere hoy (11). Ese negocio ya no la sigue: se queda en la de antes como si su dueño la hubiera elegido.

Al revés, un oficio movido que en esa ventana cambió a un giro que no es de oficio se queda en 11. `verify.sql` lo lista para revisarlo a mano. Su dueño elige la categoría en Configuración › Cómo te ven en TienditApp.

## Reglas

- Es la misma regla que la migración: un oficio cuya categoría es la que su giro daba antes de la Fase 29A y sin `DirectoryProfileUpdated` en la actividad (su dueño nunca la eligió).
- Un dueño que elige una categoría después del despliegue queda en la actividad, así que `repair.sql` no lo toca.
- Córrelo con la versión nueva en todas las instancias. Antes, la anterior puede registrar otro oficio igual.
- Si nadie se registró en la ventana, `verify.sql` sale vacío y no hay nada que hacer.

## Paso 1. Dimensionar

```
sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
```

La consulta 1 lista los que `repair.sql` mueve; la 2, los que se revisan a mano.

## Paso 2. Reparar

```
sqlcmd -S <servidor> -d <base> <autenticación> -b -W -s "|" -f o:65001 -i repair.sql -o repair-salida.txt
```

Mueve en una transacción a los de la consulta 1 y lista cada negocio con su categoría de antes. Guarda la salida en el registro del cambio. Es idempotente.

## Paso 3. Confirmar

Corre `verify.sql` otra vez: la consulta 1 sale vacía. El cambio se ve en Explorar cuando se renueva su foto (30 segundos).
