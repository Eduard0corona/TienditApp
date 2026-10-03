# Reglas de pruebas

- Un bug requiere regresión; una regla de dominio requiere casos positivo y negativo.
- No debilites assertions, omitas tests ni borres fixtures para conseguir verde.
- Tests tenant-scoped usan al menos dos tenants y prueban lectura, modificación, eliminación y descarga indebidas cuando aplica.
- No uses bases compartidas, datos reales ni secretos. Aísla y limpia recursos creados por el test.
- InMemory no demuestra semántica SQL Server; usa integración SQL Server para filtros, migraciones, rowversion, constraints y concurrencia.
- Prueba autorización por rol, permiso y capability de forma independiente.
- Mantén Domain.Tests para invariantes puras y Application.Tests para casos de uso; crea suites específicas cuando se añadan API/Infrastructure/frontend.
- Reporta el SDK usado: producción apunta a `net9.0` y los tests actuales a `net10.0`.
