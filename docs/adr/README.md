# Architecture Decision Records

Formato: contexto → decisión → consecuencias. Un ADR aceptado no se edita; si cambia la decisión se crea otro que lo sustituye.

| ADR | Título | Estado |
|---|---|---|
| [0001](0001-monolito-modular.md) | Monolito modular por capas | Aceptado |
| [0002](0002-autenticacion-cookies-sesiones-servidor.md) | Autenticación por cookie con sesiones en servidor | Aceptado |
| [0003](0003-resolucion-de-tenant.md) | Resolución del tenant por ruta + membresía | Aceptado |
| [0004](0004-aislamiento-multiempresa.md) | Aislamiento multiempresa en profundidad (EF + FK compuestas + RLS) | Aceptado |
| [0005](0005-roles-y-permisos.md) | Roles por empresa con permisos explícitos | Aceptado |
| [0006](0006-dinero-y-redondeo.md) | Dinero, precisión y redondeo | Aceptado |
| [0007](0007-inventario-y-concurrencia.md) | Inventario como libro de movimientos y concurrencia | Aceptado |
| [0008](0008-suscripciones-y-pagos.md) | Suscripciones y pagos desacoplados del proveedor | Aceptado |
| [0009](0009-auditoria.md) | Auditoría explícita y append-only | Aceptado |
| [0010](0010-migraciones-y-roles-de-base-de-datos.md) | Migraciones controladas y roles de base de datos | Aceptado |
