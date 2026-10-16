using Nexus.Domain.Common;

namespace Nexus.Domain.Tenancy;

/// <summary>
/// A named set of permissions inside one company. System roles (<see cref="SystemKey"/> set) cannot be deleted;
/// Owner cannot be edited at all. Companies may add custom roles.
/// </summary>
public sealed class Role : ITenantOwned
{
    public const int NameMaxLength = 60;
    public const int DescriptionMaxLength = 200;

    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
        Name = string.Empty;
    }

    private Role(Guid id, Guid tenantId, string name, string? description, string? systemKey, DateTimeOffset now)
    {
        Id = id;
        TenantId = tenantId;
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SystemKey = systemKey;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>One of <see cref="SystemRoles"/> keys, or null for a custom role.</summary>
    public string? SystemKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public bool IsOwner => SystemKey == SystemRoles.Owner;

    public bool IsSystem => SystemKey is not null;

    /// <summary>Owner always holds the full catalog; other roles hold their stored, known permissions.</summary>
    public IReadOnlySet<string> EffectivePermissions => EffectivePermissionsOf(SystemKey, _permissions.Select(p => p.Permission));

    public static IReadOnlySet<string> EffectivePermissionsOf(string? systemKey, IEnumerable<string> stored) =>
        systemKey == SystemRoles.Owner
            ? new HashSet<string>(Tenancy.Permissions.All, StringComparer.Ordinal)
            : new HashSet<string>(stored.Where(Tenancy.Permissions.IsKnown), StringComparer.Ordinal);

    public static Role CreateSystem(Guid tenantId, SystemRoles.Definition definition, DateTimeOffset now)
    {
        var role = new Role(Guid.CreateVersion7(), tenantId, definition.Name, definition.Description, definition.Key, now);
        if (definition.Key != SystemRoles.Owner)
        {
            role.ReplacePermissions(definition.Permissions);
        }

        return role;
    }

    public static Role CreateCustom(Guid tenantId, string name, string? description, IEnumerable<string> permissions,
        DateTimeOffset now)
    {
        var role = new Role(Guid.CreateVersion7(), tenantId, name, description, systemKey: null, now);
        role.ReplacePermissions(permissions);
        return role;
    }

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        if (IsOwner)
        {
            throw new DomainException("role_not_editable", "El rol Propietario no se puede modificar.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ReplacePermissions(permissions);
    }

    private void ReplacePermissions(IEnumerable<string> permissions)
    {
        var wanted = permissions.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.FirstOrDefault(p => !Tenancy.Permissions.IsKnown(p)) is { } unknown)
        {
            throw new DomainException("permission_unknown", $"El permiso '{unknown}' no existe.");
        }

        _permissions.RemoveAll(p => !wanted.Contains(p.Permission));
        foreach (var permission in wanted.Where(p => _permissions.All(existing => existing.Permission != p)))
        {
            _permissions.Add(new RolePermission(TenantId, Id, permission));
        }
    }
}

/// <summary>One permission granted by a role. Carries the tenant for Row-Level Security and the composite FK.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711",
    Justification = "Named after the role_permissions table; it is an entity, not a permission type.")]
public sealed class RolePermission : ITenantOwned
{
    private RolePermission()
    {
        Permission = string.Empty;
    }

    internal RolePermission(Guid tenantId, Guid roleId, string permission)
    {
        TenantId = tenantId;
        RoleId = roleId;
        Permission = permission;
    }

    public Guid TenantId { get; private set; }

    public Guid RoleId { get; private set; }

    public string Permission { get; private set; }
}
