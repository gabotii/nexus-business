# ADR-0010 — Migraciones controladas y roles de base de datos

**Decisión.**
- Dos roles de PostgreSQL:
  - `nexus` (propietario): dueño del esquema; solo lo usa el comando `migrate`.
  - `nexus_app` (ejecución): `LOGIN`, sin `SUPERUSER`, `BYPASSRLS` ni `CREATEDB`; recibe solo `SELECT, INSERT, UPDATE, DELETE` (y uso de secuencias) mediante `ALTER DEFAULT PRIVILEGES`, declarado en la migración base. Las tablas append-only revocan `UPDATE`/`DELETE`.
- El nombre del rol de ejecución es una convención fija (`nexus_app`) porque las migraciones son SQL estático; cada entorno usa su propio clúster o base de datos y su propia contraseña.
- Las migraciones de EF Core viven en `Nexus.Infrastructure/Persistence/Migrations` y se revisan en cada PR; CI comprueba que no hay cambios de modelo sin migración.
- Producción: `dotnet Nexus.Api.dll migrate` como paso explícito (servicio `migrate` de Compose que termina antes de que arranque la API) con `ConnectionStrings:Migrations`. La API nunca migra al arrancar fuera de Development. Las migraciones destructivas (borrar columnas o tablas) se hacen en dos despliegues (dejar de usar → eliminar) y con copia de seguridad previa (`docs/OPERATIONS.md`, Fase 7).
- Desarrollo local: `Database:MigrateOnStartup=true` con la cadena del propietario.
