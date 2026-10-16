# ADR-0004 — Aislamiento multiempresa en profundidad

**Contexto.** Requisito crítico: ninguna empresa puede acceder a datos de otra aunque manipule URL, IDs o peticiones. Una sola barrera ("acordarse de filtrar") no basta.

**Modelo elegido.** Base de datos compartida, esquema compartido, columna `tenant_id` en cada tabla de negocio, con estas barreras independientes:

1. **Filtro global con nombre de EF Core 10** (`HasQueryFilter("Tenant", …)`) sobre toda entidad `ITenantOwned`. Sin tenant → compara con `Guid.Empty` → cero filas. `IgnoreQueryFilters` solo en una lista blanca verificada por prueba.
2. **Interceptor de `SaveChanges`**: asigna `TenantId` en las altas desde el contexto y rechaza (excepción + log de seguridad) modificar o borrar entidades de otro tenant o cambiar `TenantId`.
3. **FK compuestas** `(tenant_id, ref_id) → (tenant_id, id)` entre tablas de negocio: la base de datos impide referencias cruzadas.
4. **Row-Level Security** en todas las tablas de negocio con `ENABLE` + `FORCE ROW LEVEL SECURITY` y la política `USING / WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)`.
   - La API se conecta con `nexus_app`: no propietario, sin `BYPASSRLS`, solo DML.
   - Un `DbConnectionInterceptor` ejecuta `set_config('app.tenant_id', …, false)` y `set_config('app.user_id', …, false)` en **cada** apertura de conexión (física o del pool), con cadena vacía cuando no hay contexto. Como se fija siempre al abrir, un valor anterior nunca sobrevive a la reutilización de la conexión.
   - EF abre y cierra la conexión por operación o por transacción dentro de la petición; el contexto de una petición nunca se usa en otra.
5. **Ámbitos explícitos para procesos de sistema** (`TenantScope`) en webhooks y tareas en segundo plano.

**Por qué RLS además de EF.** Los informes usan SQL directo para agregaciones eficientes y ahí los filtros de EF no aplican. RLS convierte un olvido en "cero filas" en lugar de una fuga.

**Pruebas exigidas.** Acceso cruzado por endpoint (lectura, edición, borrado, referencia); SQL directo como `nexus_app` sin contexto → 0 filas; alternancia de tenants sobre la misma conexión física; interceptor rechazando escrituras cruzadas; lista blanca de `IgnoreQueryFilters`.

**Limitaciones.** No protege frente al rol propietario ni frente a un DBA. Las tablas globales (`users`, `tenants`, `subscription_plans`) se protegen en la capa de aplicación. Una base de datos por tenant daría aislamiento físico pero multiplica el coste operativo; se descarta para la v1.
