# ADR-0001 — Monolito modular por capas

**Contexto.** Producto B2B nuevo, equipo pequeño, transacciones que cruzan módulos (una venta toca pedido, líneas, inventario y auditoría en la misma transacción). Se pide evitar microservicios y Kubernetes sin necesidad demostrable.

**Decisión.**
- Un único desplegable ASP.NET Core y una única base de datos PostgreSQL.
- Cuatro proyectos por capa: `Nexus.Domain`, `Nexus.Application`, `Nexus.Infrastructure`, `Nexus.Api`. Dentro de cada capa, una carpeta por módulo de negocio (Tenancy, Customers, Catalog, Inventory, Sales, Reporting, Billing, Audit, Notifications).
- Un único `DbContext`: las transacciones entre módulos son locales y ACID, sin sagas ni outbox entre módulos.
- `Application` usa directamente las abstracciones de EF Core (`DbSet`, LINQ) a través de `IAppDbContext`; **no** se crea un repositorio genérico por entidad. Las interfaces se reservan para fronteras reales: tiempo, usuario actual, tenant, correo, archivos, proveedor de pagos.
- Las dependencias entre módulos siguen el diagrama de `ARCHITECTURE.md` §2 sin ciclos. Las pruebas de arquitectura comprueban las referencias entre ensamblados (Domain no referencia EF ni ASP.NET Core; Application no referencia Npgsql ni ASP.NET Core).

**Consecuencias.**
- (+) Simplicidad operativa, transacciones fuertes, refactorización barata.
- (−) Las fronteras entre módulos dentro de una capa son por convención y revisión; si un módulo necesitara separarse, su carpeta y su contrato público son el punto de corte.
- (−) El rate limiting y la caché de sesiones son por instancia. Con más de una réplica habrá que introducir Redis (u otro almacén compartido); hasta entonces no está justificado.
