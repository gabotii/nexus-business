# ADR-0005 — Roles por empresa con permisos explícitos

**Decisión.**
- Los endpoints exigen **permisos**, nunca nombres de rol. Catálogo inicial (en código, `Permissions`):
  `settings.manage`, `members.view`, `members.manage`, `roles.manage`, `customers.view`, `customers.manage`, `customers.export`,
  `products.view`, `products.manage`, `products.view_cost`, `inventory.view`, `inventory.adjust`, `sales.view`, `sales.create`,
  `sales.cancel`, `reports.view`, `reports.export`, `audit.view`, `billing.manage`.
- Cada empresa recibe al crearse cinco roles de sistema. Owner tiene todos los permisos y no es editable; los demás son editables y la empresa puede crear roles personalizados.

| Permiso | Owner | Administrator | Manager | Employee | ReadOnly |
|---|:-:|:-:|:-:|:-:|:-:|
| billing.manage | ✔ | | | | |
| settings.manage, roles.manage, members.manage | ✔ | ✔ | | | |
| members.view, audit.view | ✔ | ✔ | ✔ | | |
| customers.view, products.view, inventory.view, sales.view | ✔ | ✔ | ✔ | ✔ | ✔ |
| customers.manage, sales.create | ✔ | ✔ | ✔ | ✔ | |
| products.manage, products.view_cost, inventory.adjust, sales.cancel | ✔ | ✔ | ✔ | | |
| reports.view | ✔ | ✔ | ✔ | | ✔ |
| reports.export, customers.export | ✔ | ✔ | ✔ | | |

- **Reglas anti-escalada** (servidor): nadie cambia su propio rol ni se desactiva a sí mismo; solo se asignan o editan roles cuyos permisos ⊆ permisos propios; solo un Owner asigna o retira el rol Owner; la empresa conserva siempre ≥ 1 Owner activo; los cambios de permisos se auditan.
- Los permisos efectivos se leen de la base de datos en cada petición (ADR-0003): revocar un permiso tiene efecto inmediato.
- El SPA recibe la lista de permisos de la membresía para mostrar u ocultar acciones; es solo UX.
