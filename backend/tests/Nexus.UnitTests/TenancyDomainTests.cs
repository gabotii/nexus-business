using System.Net;
using Nexus.Application.Tenancy;
using Nexus.Domain.Common;
using Nexus.Domain.Tenancy;
using Nexus.Infrastructure.Common;
using Nexus.Infrastructure.Email;
using static Nexus.Domain.Tenancy.Permissions;

namespace Nexus.UnitTests;

public sealed class TenantTests
{
    [Theory]
    [InlineData("Panadería Núñez & Hijos", "panaderia-nunez-hijos")]
    [InlineData("  ACME  S.L. ", "acme-s-l")]
    [InlineData("Ça va? -- Ñandú 2026!", "ca-va-nandu-2026")]
    [InlineData("¿¿??", "empresa")]
    [InlineData("株式会社", "empresa")]
    public void Slugs_keep_only_lowercase_ascii_letters_digits_and_single_hyphens(string name, string slug) =>
        Assert.Equal(slug, Tenant.SlugFrom(name));

    [Fact]
    public void Long_names_leave_room_for_a_disambiguation_suffix()
    {
        var slug = Tenant.SlugFrom(new string('a', 30) + " " + new string('b', 30));

        Assert.True(slug.Length <= Tenant.SlugMaxLength - 5);
        Assert.DoesNotMatch("-$", slug);
    }

    [Fact]
    public void Unsupported_currencies_are_rejected()
    {
        var error = Assert.Throws<DomainException>(() =>
            new Tenant(Guid.NewGuid(), "Empresa", "empresa", "XYZ", "Europe/Madrid", Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Equal("currency_not_supported", error.Code);
    }

    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("CLP", 0)]
    [InlineData("JPY", 0)]
    public void Currencies_carry_their_minor_units(string code, int decimals) =>
        Assert.Equal(decimals, Currencies.MinorUnits(code));
}

public sealed class RoleTests
{
    /// <summary>The matrix of ADR-0005, written out independently of <see cref="SystemRoles"/>.</summary>
    public static TheoryData<string, string[]> AdrMatrix => new()
    {
        { SystemRoles.Owner, [.. All] },
        {
            SystemRoles.Administrator,
            [
                SettingsManage, RolesManage, MembersManage, MembersView, AuditView, CustomersView, ProductsView,
                InventoryView, SalesView, CustomersManage, SalesCreate, ProductsManage, ProductsViewCost, InventoryAdjust,
                SalesCancel, ReportsView, ReportsExport, CustomersExport,
            ]
        },
        {
            SystemRoles.Manager,
            [
                MembersView, AuditView, CustomersView, ProductsView, InventoryView, SalesView, CustomersManage, SalesCreate,
                ProductsManage, ProductsViewCost, InventoryAdjust, SalesCancel, ReportsView, ReportsExport, CustomersExport,
            ]
        },
        { SystemRoles.Employee, [CustomersView, ProductsView, InventoryView, SalesView, CustomersManage, SalesCreate] },
        { SystemRoles.ReadOnly, [CustomersView, ProductsView, InventoryView, SalesView, ReportsView] },
    };

    [Theory]
    [MemberData(nameof(AdrMatrix))]
    public void System_roles_match_the_permission_matrix(string key, string[] expected)
    {
        var role = Role.CreateSystem(Guid.NewGuid(), SystemRoles.Get(key), DateTimeOffset.UtcNow);

        Assert.Equal(expected.Order(), role.EffectivePermissions.Order());
    }

    [Fact]
    public void Owner_holds_the_whole_catalog_without_stored_rows()
    {
        var owner = Role.CreateSystem(Guid.NewGuid(), SystemRoles.Get(SystemRoles.Owner), DateTimeOffset.UtcNow);

        Assert.Empty(owner.Permissions);
        Assert.Equal(All.Order(), owner.EffectivePermissions.Order());
    }

    [Fact]
    public void Owner_cannot_be_edited()
    {
        var owner = Role.CreateSystem(Guid.NewGuid(), SystemRoles.Get(SystemRoles.Owner), DateTimeOffset.UtcNow);

        Assert.Equal("role_not_editable", Assert.Throws<DomainException>(() => owner.Update("Jefe", null, [])).Code);
    }

    [Fact]
    public void Unknown_permissions_are_refused_and_stored_unknowns_are_ignored()
    {
        Assert.Equal("permission_unknown", Assert.Throws<DomainException>(() =>
            Role.CreateCustom(Guid.NewGuid(), "Raro", null, ["root.everything"], DateTimeOffset.UtcNow)).Code);

        Assert.Equal([SalesView], Role.EffectivePermissionsOf(null, [SalesView, "legacy.permission"]));
    }

    [Fact]
    public void Updating_permissions_keeps_the_rows_that_stay()
    {
        var role = Role.CreateCustom(Guid.NewGuid(), "Cajero", " ", [SalesView, SalesCreate], DateTimeOffset.UtcNow);
        var kept = role.Permissions.Single(p => p.Permission == SalesView);

        role.Update(" Caja ", "Ventas en tienda", [SalesView, CustomersView, CustomersView]);

        Assert.Equal("Caja", role.Name);
        Assert.Equal("Ventas en tienda", role.Description);
        Assert.Same(kept, role.Permissions.Single(p => p.Permission == SalesView));
        Assert.Equal([CustomersView, SalesView], role.Permissions.Select(p => p.Permission).Order());
        Assert.All(role.Permissions, p => Assert.Equal(role.TenantId, p.TenantId));
    }
}

public sealed class MembershipAndInvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Disabling_twice_is_refused_and_enabling_restores_access_with_a_role()
    {
        var membership = new Membership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        membership.Disable(Now.AddDays(1));

        Assert.Equal("member_already_disabled", Assert.Throws<DomainException>(() => membership.Disable(Now)).Code);

        var role = Guid.NewGuid();
        membership.Enable(role, Now.AddDays(2));
        Assert.True(membership.IsActive);
        Assert.Null(membership.DisabledAt);
        Assert.Equal(role, membership.RoleId);
        Assert.Equal(Now.AddDays(2), membership.JoinedAt);
    }

    [Fact]
    public void Invitations_expire_after_seven_days()
    {
        var invitation = NewInvitation();

        Assert.True(invitation.IsPending(Now.AddDays(7).AddSeconds(-1)));
        Assert.False(invitation.IsPending(Now.AddDays(7)));
        Assert.Equal("invitation_not_pending",
            Assert.Throws<DomainException>(() => invitation.Accept(Guid.NewGuid(), Now.AddDays(8))).Code);
    }

    [Fact]
    public void Accepted_or_revoked_invitations_can_be_neither_renewed_nor_reused()
    {
        var accepted = NewInvitation();
        accepted.Accept(Guid.NewGuid(), Now.AddHours(1));
        var revoked = NewInvitation();
        revoked.Revoke(Now.AddHours(1));

        Assert.Throws<DomainException>(() => accepted.Accept(Guid.NewGuid(), Now.AddHours(2)));
        Assert.Throws<DomainException>(() => accepted.Renew([1], Now));
        Assert.Throws<DomainException>(() => revoked.Revoke(Now));
        Assert.Throws<DomainException>(() => revoked.Accept(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Renewing_restarts_the_expiry_with_a_new_token()
    {
        var invitation = NewInvitation();

        invitation.Renew([9, 9], Now.AddDays(6));

        Assert.Equal(Now.AddDays(13), invitation.ExpiresAt);
        Assert.Equal([9, 9], invitation.TokenHash);
    }

    private static Invitation NewInvitation() =>
        new(Guid.NewGuid(), " ana@example.test ", "ANA@EXAMPLE.TEST", Guid.NewGuid(), [1, 2], Guid.NewGuid(), Now);
}

public sealed class TenantContextTests
{
    [Fact]
    public void A_system_scope_has_no_permissions_and_restores_the_previous_state()
    {
        var context = new TenantContext();
        var outer = Guid.NewGuid();
        context.BeginResolution(outer);
        context.SetMembership(Guid.NewGuid(), Guid.NewGuid(), new HashSet<string> { SalesView });

        using (context.BeginSystemScope(Guid.NewGuid()))
        {
            Assert.False(context.IsMember);
            Assert.False(context.Has(SalesView));
            Assert.Throws<Nexus.Application.Common.ForbiddenException>(() => context.Demand(SalesView));
        }

        Assert.Equal(outer, context.TenantId);
        Assert.True(context.Has(SalesView));
    }

    [Fact]
    public void Without_a_membership_nothing_is_permitted()
    {
        var context = new TenantContext();

        Assert.Equal(Guid.Empty, context.TenantId);
        Assert.False(context.Has(SalesView));
    }
}

public sealed class PlatformHelperTests
{
    [Theory]
    [InlineData("203.0.113.77", "203.0.113.0/24")]
    [InlineData("::ffff:198.51.100.9", "198.51.100.0/24")]
    [InlineData("2001:db8:abcd:12:1:2:3:4", "2001:db8:abcd::/48")]
    public void Client_addresses_are_truncated_before_storage(string address, string prefix) =>
        Assert.Equal(prefix, IpPrefix.From(IPAddress.Parse(address)));

    [Fact]
    public void Failed_e_mails_back_off_exponentially_and_drop_the_body_when_abandoned()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new EmailMessage { BodyText = "link with token", NextAttemptAt = now };

        message.MarkFailed("timeout", now);
        Assert.Equal(now.AddMinutes(1), message.NextAttemptAt);
        message.MarkFailed("timeout", now);
        Assert.Equal(now.AddMinutes(2), message.NextAttemptAt);

        for (var i = 2; i < EmailMessage.MaxAttempts; i++)
        {
            message.MarkFailed(new string('x', 900), now);
        }

        Assert.Equal(now, message.FailedAt);
        Assert.Null(message.BodyText);
        Assert.Equal(500, message.LastError!.Length);
    }
}
