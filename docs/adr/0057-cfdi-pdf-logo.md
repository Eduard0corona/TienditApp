# ADR 0057: El logo del negocio en el PDF de sus CFDI

- Estado: Aceptada
- Fecha: 2026-10-30

## Contexto

Desde la Fase 28A (ADR 0045), el PDF de cada CFDI lo arma SW Sapien con su plantilla genérica de CFDI 4.0 (`cfdi40`), sin el logo del negocio. Es lo que el negocio descarga en Pedidos y lo que su cliente recibe por correo (ADR 0049), y quedó como pendiente en la deuda técnica.

Lo que sabemos de SW Sapien:

- `pdf/v1/api/GeneratePdf` recibe el XML timbrado, la plantilla y, opcionalmente, el logo en base64 (`logo`).
- Solo las plantillas genéricas lo toman: `cfdi40` (ingreso y egreso), `payment20` (pagos), `payroll40` y `billoflading40`. Una plantilla personalizada se da de alta en su portal, con la cuenta de la plataforma.
- Hasta ahora el complemento de pago usaba `cfdi40`, que no es la plantilla de Pagos 2.0.

Cada negocio ya sube su logo en Configuración › Negocio. TienditApp guarda cinco variantes WebP en la carpeta pública del negocio (`logos/{tenantId}`), y `Tenant.LogoUrl` es la dirección base de todas.

Tomé las decisiones de la Fase 28L con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **El logo es el de Configuración › Negocio.** No hay otro logo para facturas, ni uno por sucursal.
2. **Se lee de la carpeta del negocio, y de ningún otro lado** (`ICfdiPdfLogos`, Infrastructure).
   - `IFileStorageService.OpenReadAsync` abre un archivo con las mismas reglas que borrarlo: solo dentro de la carpeta que se le pide, sin rutas que salgan de ella. El almacenamiento local y Azure Blob la implementan, y una dirección de otro contenedor o de otro negocio da `null` antes de leer.
   - Se pide `LogoUrl` + `_md.webp` (512 px de ancho) en `logos/{tenantId}`, con el `tenantId` del CFDI. Un `LogoUrl` que apunte a la carpeta de otro negocio no pone su logo: el PDF sale sin logo.
   - El negocio se lee en un scope propio, porque el gateway vive lo que la aplicación.
3. **Se manda como PNG sin metadatos, de hasta 400 × 200** (`ImageProcessingService.ToPngAsync`). Conserva su proporción y su transparencia, y nunca se agranda. Se decodifica con los mismos límites que una imagen subida (formatos aceptados, primer cuadro y 50 megapíxeles).
4. **`IInvoicingGateway.RenderPdfAsync` recibe el negocio del CFDI** y el gateway agrega el logo. Así lo llevan todos los PDF, sin cambiar los casos de uso:
   - los cuatro tipos de CFDI: la factura de un pedido, la global, el complemento de pago y la nota de crédito;
   - al timbrar (`OrderInvoiceStamper`), al descargar uno que no tenía PDF (`InvoiceFiles`) y en el correo que lo espera (`CfdiEmailRequestedHandler`).
5. **El complemento de pago usa `payment20`**, la plantilla genérica de Pagos 2.0, que toma el logo. Los demás siguen con `cfdi40`. La plantilla se elige por el `TipoDeComprobante` del XML timbrado.
6. **El logo nunca detiene el PDF.**
   - Sin logo, con uno que ya no está o con uno que no se puede leer (otra carpeta, un archivo que no es imagen, el almacenamiento que falla), el PDF sale sin logo. Queda una advertencia en el log con el negocio y el tipo de error, nunca la dirección del archivo. Cancelar la solicitud sí la cancela.
   - Si SW Sapien rechaza la petición (un 4xx que no es de la cuenta, de tiempo ni de demasiadas peticiones) y llevaba el logo o la plantilla de Pagos, se manda una vez la petición de antes de esta fase: `cfdi40` y sin logo. Queda una advertencia con el negocio y el estado. Si SW falla (5xx) o no responde, no se repite: es el error de siempre, y el PDF se arma después (ADR 0049).
7. **Los PDF que ya se guardaron no cambian.** El PDF se arma y se guarda al timbrar; un logo nuevo solo sale en los que se arman después (los timbrados después, o uno que no tenía PDF y se descarga o se manda).
8. **Configuración › Facturación lo dice.** La tarjeta «Tu negocio» muestra «Logo en el PDF»: el logo, o «Sin logo» con «Sube el logo de tu negocio», que lleva a Configuración › Negocio. «Cómo funciona» dice que los PDF que ya se armaron se quedan como están.
9. **Sin migración ni cambios en el contrato de la API.**
10. **Fuera de esta fase:**
    - volver a armar los PDF ya guardados con el logo nuevo;
    - un logo para facturas distinto del del negocio, o uno por sucursal;
    - plantillas personalizadas (colores, leyendas, otro acomodo).

## Consecuencias

- **Lo que logra:** el cliente recibe la factura con el logo del negocio que se la emite, y el negocio la descarga igual.
- **Lo que limita:**
  - Cada PDF lee el logo del almacenamiento y lo convierte (una variante de 512 px, una vez por PDF). Es poco: los PDF se arman al timbrar y, solo si faltan, al descargar o mandar.
  - Un logo muy alargado se ve chico en el recuadro de la plantilla, que fija SW Sapien.
  - Si SW Sapien deja de aceptar el logo o la plantilla de Pagos, el PDF sale como antes de esta fase, sin logo, y cada uno cuesta una petición más hasta que se corrija. La advertencia del log lo deja ver.
- **Compatibilidad durante el despliegue:** no cambia el contrato. El cliente anterior no muestra la fila del logo; el nuevo con la API anterior la muestra, pero el PDF sale sin logo hasta que llegue la API nueva.
- **Reversibilidad:** sin cambios en la base. Volver a la versión anterior quita el logo de los PDF que se armen desde entonces; los que ya lo llevan se quedan así.
