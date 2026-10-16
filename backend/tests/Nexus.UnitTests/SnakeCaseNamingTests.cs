using Nexus.Infrastructure.Persistence;

namespace Nexus.UnitTests;

public sealed class SnakeCaseNamingTests
{
    [Theory]
    [InlineData("SalesOrderLine", "sales_order_line")]
    [InlineData("TenantId", "tenant_id")]
    [InlineData("PK_AspNetUsers", "pk_asp_net_users")]
    [InlineData("IX_SalesOrders_TenantId_Number", "ix_sales_orders_tenant_id_number")]
    [InlineData("HTTPRequest", "http_request")]
    [InlineData("Sha256", "sha256")]
    [InlineData("Sha256Hash", "sha256_hash")]
    [InlineData("already_snake", "already_snake")]
    [InlineData("__ef_migrations_history", "__ef_migrations_history")]
    [InlineData("", "")]
    public void Converts_identifiers(string input, string expected) =>
        Assert.Equal(expected, SnakeCaseNaming.ToSnakeCase(input));
}
