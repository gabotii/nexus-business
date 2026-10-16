# ADR-0009 — Auditoría explícita y append-only

**Decisión.**
- Los casos de uso llaman explícitamente a `IAuditLog.Record(action, entity, metadata)` (no se vuelca el *change tracker* entero): acciones con nombre estable (`sales.order.confirmed`, `members.role.changed`…) y metadatos de una lista blanca por acción (p. ej. precio anterior/nuevo, rol anterior/nuevo).
- La entrada se escribe en la **misma transacción** que el cambio: se guardan ambos o ninguno.
- Campos: actor (usuario o sistema), tenant (null para eventos de cuenta: login, fallo de login, cambio de contraseña), acción, entidad, fecha UTC, correlation id (trace id W3C) e IP truncada.
- Prohibido registrar contraseñas, tokens, cuerpos de petición, secretos o notas libres.
- `nexus_app` solo tiene `INSERT` y `SELECT` sobre `audit_log` (sin `UPDATE`, `DELETE` ni `TRUNCATE`): ni un error de la aplicación puede alterar el historial. RLS limita la lectura al tenant (y a los eventos de cuenta del propio usuario).
- Retención indefinida en la v1; la política de retención y purga (ejecutada por el rol propietario) se define en la Fase 7.
