# ADR 0093: La cotización en PDF

- Estado: Aceptada en la Fase 31M (2026-11-13).
- Fecha: 2026-11-13
- Continúa: [ADR 0053](0053-home-visit-quotes.md) (oficios a domicilio, la visita para cotizar), entrada «PDF de la cotización y documento adjunto por WhatsApp» de «Después».

## Contexto

- **La cotización solo vivía en su enlace:** el correo la llevaba en el cuerpo y el WhatsApp con el enlace. El cliente no tenía un archivo que guardar, imprimir o reenviar a quien paga, y el negocio no tenía uno que mandar por su cuenta.
- **Esperaba una decisión del dueño del producto** (punto 14 de la lista): qué biblioteca de PDF y con qué licencia. Las que se revisaron:
  - **PDFsharp 6.5** declara `AGPL-3.0-only` en NuGet y pide una licencia al crear un documento (revisado el 2026-11-03), e **iText** es AGPL: obligarían a publicar el código de TienditApp o a pagar una licencia comercial.
  - **QuestPDF** es gratis solo para negocios que facturan menos de un millón de dólares al año; arriba de eso se paga, y la cuenta es la de TienditApp, no la de cada negocio.
  - **PdfPig 0.1.16** (Apache-2.0) sabe leer bien, pero al escribir con las 14 fuentes de todo lector no admite letras con acento: se detiene en la «ó». Con fuentes TrueType habría que llevar el archivo de la fuente dentro de cada PDF.
- El PDF de una cotización es una hoja con texto, líneas y un logo: no necesita una biblioteca entera.

El dueño del producto pidió avanzar los puntos de la lista sin esperar su respuesta a cada decisión. Tomé las de la Fase 31M con esa autorización. **No lleva migración.**

## Decisión

1. **TienditApp escribe sus propios PDF** (`Infrastructure/Documents`): un escritor de PDF 1.4 (unas 430 líneas con el manejo del texto, más el diseño de la cotización), sin biblioteca de PDF, con páginas carta, texto, líneas, rectángulos e imágenes RGB con transparencia, el contenido comprimido y su tabla de referencias. El logo se lee con ImageSharp, como ya se leía para el PDF de los CFDI.
   - **Fuentes:** Helvetica y Helvetica negrita, dos de las 14 que tiene todo lector de PDF, en WinAnsiEncoding: el español (acentos, ñ, ¿, ¡) no necesita fuente incrustada, y un PDF pesa unos 5 KB más el logo.
   - **Medidas:** los anchos de cada letra salen de los archivos AFM de Adobe para esas fuentes, que Adobe deja usar y redistribuir sin costo; con ellos se alinean los montos a la derecha y se parte el texto en renglones.
   - **Lo que la codificación no tiene** se vuelve la letra más parecida que sí tiene (ő es o, ł es l), un espacio, nada (un emoji u otro símbolo, como ✅ o ★, sin dejar espacios dobles) o un signo de interrogación (otro alfabeto): el PDF nunca falla por un nombre.
   - **Nada se sale de la página:** lo que incluye un renglón se escribe en un solo párrafo (sus saltos de línea son espacios, así un renglón nunca crece más que una página), y el nombre del negocio (dos renglones), el del cliente (tres), el teléfono y el pie se cortan con «…» donde se acaba su lugar.
   - **PdfPig (Apache-2.0) entra solo a las pruebas**, para leer el PDF de vuelta con un lector independiente: el texto, las páginas, la imagen y su máscara.
2. **Lo que dice** es lo que dice la página de la cotización: el negocio (su logo cuando lo tiene —el mismo del PDF de sus CFDI, [ADR 0057](0057-cfdi-pdf-logo.md)—, su nombre y su teléfono; no su correo de contacto, que empieza siendo el de quien registró el negocio y la página tampoco muestra), «Cotización COT-…» y su versión, cómo está (borrador, aceptada o rechazada con su día, vencida, reemplazada, descartada; una enviada vigente no lo repite), para quién, cuándo se envió y hasta cuándo vale, sus renglones (la tabla sigue en la página siguiente con su encabezado), subtotal, IVA, total, la visita que se descuenta y lo que queda por pagar, si los precios incluyen el IVA y su nota. Cada página dice su número y cuántas son. **Del cliente, solo su nombre**: nunca su teléfono ni su correo.
3. **El negocio descarga el de cualquier versión** (`GET /api/v1/quotes/{id}/pdf`, «Descargar PDF» en el detalle): también un borrador, para revisarlo antes de enviarlo; con cambios sin guardar el botón pide guardar primero, porque el PDF dice lo guardado. Lo pide quien maneja las cotizaciones (`ManageOrders` con servicios o citas); el de otro negocio no existe (404).
4. **El cliente descarga el de su enlace** (`POST /api/v1/public/quotes/pdf`, «Descargar PDF» en su página): esa versión, mientras su enlace abre (hasta 30 días después de su vigencia), con el límite de peticiones del menú público. El enlace va en el cuerpo, nunca en la dirección. Uno desconocido, cambiado o vencido no existe (404), como al abrirlo.
5. **Las dos descargas** van con `Content-Security-Policy: sandbox` y `Cache-Control: private, no-store`, como los CFDI: se descargan, nunca se abren como una página de TienditApp ni se guardan en un caché compartido. El archivo se llama `cotizacion-COT-000123.pdf`, o `cotizacion-COT-000123-v2.pdf` desde la segunda versión.
6. **El correo de la cotización lleva el PDF adjunto** y dice «La cotización va adjunta en PDF.» Si el PDF no se puede generar en 10 segundos (el logo se lee del almacenamiento), el correo sale sin él (con su enlace, como antes): nunca tarde ni perdido por el PDF.
7. **El WhatsApp de la cotización lleva el PDF cuando va sin plantilla** (a quien escribió en las últimas 24 horas): el PDF como documento y el texto del mensaje como su pie. **Con la plantilla del negocio va como antes, sin PDF:** su plantilla puede no tener encabezado de documento, y Meta rechaza uno mandado a una que no lo tiene. Sale el texto solo si el PDF no se puede generar (en los mismos 10 segundos), si el texto pasa de los 1,024 caracteres que admite el pie de un documento, o si WhatsApp no toma el PDF al subirlo (nada le llegó al cliente). Si WhatsApp tomó el PDF y rechazó el mensaje, ese es su resultado: mandar el texto después podría hacerle llegar la cotización dos veces.

## Consecuencias

- **Lo que logra:** la cotización es un archivo que el cliente guarda, imprime o reenvía, que llega con el correo y con el WhatsApp, y que el negocio descarga de cualquier versión. Sin una licencia que decidir ni una dependencia que mantener.
- **Lo que limita:**
  - **Solo dos fuentes y un diseño:** el PDF no toma los colores del tema del negocio ni otra tipografía.
  - **Otros alfabetos** (chino, árabe, cirílico) salen como signos de interrogación; un emoji u otro símbolo no sale. Con fuentes incrustadas saldrían, a cambio de PDF de cientos de KB.
  - **Un nombre muy largo se corta** con «…» (el negocio en dos renglones, el cliente en tres), y lo que incluye un renglón pierde sus saltos de línea.
  - **Sin el correo del negocio:** quien lo quiera en el papel lo escribe en la nota.
  - **Con plantilla de WhatsApp no va el PDF:** pediría una plantilla aprobada con encabezado de documento.
  - **El PDF no lleva el enlace** de la cotización: un papel dura más que el enlace, y el enlace es la llave para responderla. Va en el correo y en el WhatsApp.
  - **Se genera cada vez** que se pide: no se guarda, así que dice la cotización como está ahora (una enviada que venció dice que venció).
  - El escritor solo sabe lo que este PDF necesita; un documento más rico (tablas con celdas combinadas, formularios, firma digital) pediría crecerlo o, entonces sí, una biblioteca.
- **Decisiones por omisión** (sin preferencia expresa del dueño): escribir el PDF en lugar de comprar una licencia; hoja carta; el borrador descargable; sin el enlace en el papel; el PDF en el WhatsApp solo sin plantilla.
- **Reversibilidad:** no cambia la base. Volver a la versión anterior quita las descargas, el adjunto y el documento del WhatsApp.
