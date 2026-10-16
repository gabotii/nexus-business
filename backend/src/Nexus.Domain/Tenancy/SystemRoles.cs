using static Nexus.Domain.Tenancy.Permissions;

namespace Nexus.Domain.Tenancy;

/// <summary>
/// The five roles every company receives when it is created (ADR-0005 matrix). Owner always holds the whole
/// catalog, computed from code, so permissions added in later versions reach every Owner without a data migration.
/// The other system roles are seeded with these defaults and may then be edited by the company.
/// </summary>
public static class SystemRoles
{
    public const string Owner = "owner";
    public const string Administrator = "administrator";
    public const string Manager = "manager";
    public const string Employee = "employee";
    public const string ReadOnly = "read_only";

    public sealed record Definition(string Key, string Name, string Description, IReadOnlyList<string> Permissions);

    private static readonly string[] EveryoneViews = [CustomersView, ProductsView, InventoryView, SalesView];

    public static IReadOnlyList<Definition> Defaults { get; } =
    [
        new(Owner, "Propietario", "Control total de la empresa, incluida la suscripción.", Permissions.All),
        new(Administrator, "Administrador", "Gestiona la empresa, el equipo y los roles.",
            Permissions.All.Where(p => p != BillingManage).ToArray()),
        new(Manager, "Responsable", "Gestiona catálogo, inventario y ventas; consulta el equipo y la auditoría.",
        [
            MembersView, AuditView, .. EveryoneViews, CustomersManage, SalesCreate,
            ProductsManage, ProductsViewCost, InventoryAdjust, SalesCancel, ReportsView, ReportsExport, CustomersExport,
        ]),
        new(Employee, "Empleado", "Atiende clientes y registra ventas.", [.. EveryoneViews, CustomersManage, SalesCreate]),
        new(ReadOnly, "Solo lectura", "Consulta datos e informes sin modificar nada.", [.. EveryoneViews, ReportsView]),
    ];

    public static Definition Get(string key) => Defaults.Single(d => d.Key == key);
}
