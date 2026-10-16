/**
 * Labels for the permission catalog (ADR-0005; the API's Permissions.All is the source of truth). The SPA uses
 * permissions to decide what to show, never to decide what is allowed.
 */
export const PERMISSION_GROUPS: { label: string; permissions: { key: string; label: string }[] }[] = [
  {
    label: 'Empresa',
    permissions: [
      { key: 'settings.manage', label: 'Configurar la empresa' },
      { key: 'billing.manage', label: 'Gestionar la suscripción' },
      { key: 'audit.view', label: 'Ver la auditoría' },
    ],
  },
  {
    label: 'Equipo',
    permissions: [
      { key: 'members.view', label: 'Ver el equipo' },
      { key: 'members.manage', label: 'Invitar y gestionar miembros' },
      { key: 'roles.manage', label: 'Crear y editar roles' },
    ],
  },
  {
    label: 'Clientes',
    permissions: [
      { key: 'customers.view', label: 'Ver clientes' },
      { key: 'customers.manage', label: 'Crear y editar clientes' },
      { key: 'customers.export', label: 'Exportar clientes' },
    ],
  },
  {
    label: 'Catálogo e inventario',
    permissions: [
      { key: 'products.view', label: 'Ver productos' },
      { key: 'products.manage', label: 'Crear y editar productos' },
      { key: 'products.view_cost', label: 'Ver costes' },
      { key: 'inventory.view', label: 'Ver inventario' },
      { key: 'inventory.adjust', label: 'Ajustar existencias' },
    ],
  },
  {
    label: 'Ventas e informes',
    permissions: [
      { key: 'sales.view', label: 'Ver ventas' },
      { key: 'sales.create', label: 'Registrar ventas' },
      { key: 'sales.cancel', label: 'Cancelar ventas' },
      { key: 'reports.view', label: 'Ver informes' },
      { key: 'reports.export', label: 'Exportar informes' },
    ],
  },
]

const labels = new Map(PERMISSION_GROUPS.flatMap((g) => g.permissions.map((p) => [p.key, p.label] as const)))

export const permissionLabel = (key: string) => labels.get(key) ?? key

export const ALL_PERMISSIONS = [...labels.keys()]

export const OWNER_ROLE = 'owner'
