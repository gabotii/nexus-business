namespace Nexus.Domain.Tenancy;

/// <summary>
/// The permission catalog (ADR-0005). Endpoints and use cases demand permissions, never role names. Stored role
/// permissions outside this catalog are ignored when a membership is resolved.
/// </summary>
public static class Permissions
{
    public const string SettingsManage = "settings.manage";
    public const string MembersView = "members.view";
    public const string MembersManage = "members.manage";
    public const string RolesManage = "roles.manage";
    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";
    public const string CustomersExport = "customers.export";
    public const string ProductsView = "products.view";
    public const string ProductsManage = "products.manage";
    public const string ProductsViewCost = "products.view_cost";
    public const string InventoryView = "inventory.view";
    public const string InventoryAdjust = "inventory.adjust";
    public const string SalesView = "sales.view";
    public const string SalesCreate = "sales.create";
    public const string SalesCancel = "sales.cancel";
    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";
    public const string AuditView = "audit.view";
    public const string BillingManage = "billing.manage";

    public static IReadOnlyList<string> All { get; } =
    [
        SettingsManage, MembersView, MembersManage, RolesManage,
        CustomersView, CustomersManage, CustomersExport,
        ProductsView, ProductsManage, ProductsViewCost,
        InventoryView, InventoryAdjust,
        SalesView, SalesCreate, SalesCancel,
        ReportsView, ReportsExport,
        AuditView, BillingManage,
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    public static bool IsKnown(string permission) => Known.Contains(permission);
}
