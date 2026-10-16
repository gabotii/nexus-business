using Nexus.Application.Common;
using Nexus.Domain.Catalog;
using Nexus.Domain.Common;
using Nexus.Domain.Customers;
using Nexus.Domain.Files;

namespace Nexus.UnitTests;

public sealed class NormalizationTests
{
    [Theory]
    [InlineData("  Panadería   NÚÑEZ ", "panaderia nunez")]
    [InlineData("Ça\tva\nbien", "ca va bien")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Search_text_ignores_case_accents_and_extra_spaces(string? input, string expected) =>
        Assert.Equal(expected, TextNormalization.ForSearch(input));

    [Theory]
    [InlineData("B-12.345.678", "B12345678")]
    [InlineData(" 12 345 678-z ", "12345678Z")]
    [InlineData("ES/B12345678", "ESB12345678")]
    [InlineData(" - ", null)]
    [InlineData(null, null)]
    public void Tax_ids_compare_without_separators(string? input, string? expected) =>
        Assert.Equal(expected, TextNormalization.TaxId(input));

    [Fact]
    public void Like_patterns_escape_wildcards()
    {
        Assert.Equal("%50\\%%", Paging.ContainsPattern("50%"));
        Assert.Equal("%a\\_b%", Paging.ContainsPattern("a_b"));
        Assert.Equal("%c:\\\\x%", Paging.ContainsPattern("c:\\x"));
    }
}

public sealed class AmountTests
{
    [Theory]
    [InlineData("0", true)]
    [InlineData("3.1416", true)]
    [InlineData("3.14159", false)]
    [InlineData("-0.01", false)]
    [InlineData("999999999999999.9999", true)]
    public void Money_has_at_most_four_decimals_and_is_never_negative(string value, bool valid) =>
        Assert.Equal(valid, Amounts.IsValidMoney(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("21", true)]
    [InlineData("4.75", true)]
    [InlineData("4.755", false)]
    [InlineData("100.01", false)]
    public void Tax_rates_are_percentages_with_two_decimals(string value, bool valid) =>
        Assert.Equal(valid, Amounts.IsValidRate(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Trailing_zeros_do_not_count_as_precision() => Assert.True(Amounts.IsValidMoney(1.50000m));
}

public sealed class CustomerDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static CustomerDetails Details(string name = "Panadería Núñez", string? email = "Ventas@Nunez.Example") =>
        new(CustomerKind.Company, name, "  ", " B-12.345.678 ", email, " 600 000 000 ",
            new PostalAddress(" Calle Mayor 1 ", "Madrid", " ", null, "es"));

    [Fact]
    public void Derived_fields_are_normalised_and_blanks_become_null()
    {
        var customer = new Customer(Guid.NewGuid(), Details(), Guid.NewGuid(), Now);

        Assert.Null(customer.LegalName);
        Assert.Equal("B-12.345.678", customer.TaxId);
        Assert.Equal("B12345678", customer.NormalizedTaxId);
        Assert.Equal("VENTAS@NUNEZ.EXAMPLE", customer.NormalizedEmail);
        Assert.Equal("Calle Mayor 1", customer.AddressLine);
        Assert.Null(customer.PostalCode);
        Assert.Equal("ES", customer.CountryCode);
        Assert.Contains("panaderia nunez", customer.SearchText, StringComparison.Ordinal);
        Assert.Contains("b12345678", customer.SearchText, StringComparison.Ordinal);
    }

    [Fact]
    public void Tags_are_replaced_as_a_set_with_a_limit()
    {
        var customer = new Customer(Guid.NewGuid(), Details(), Guid.NewGuid(), Now);
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        customer.SetTags([a, b]);
        customer.SetTags([b, c]);

        Assert.Equal(new[] { b, c }.Order(), customer.Tags.Select(t => t.TagId).Order());
        Assert.All(customer.Tags, t => Assert.Equal(customer.TenantId, t.TenantId));
        Assert.Equal("too_many_tags", Assert.Throws<DomainException>(() =>
            customer.SetTags([.. Enumerable.Range(0, Customer.MaxTags + 1).Select(_ => Guid.NewGuid())])).Code);
    }

    [Fact]
    public void Archiving_twice_or_restoring_an_active_customer_is_refused()
    {
        var customer = new Customer(Guid.NewGuid(), Details(), Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(customer.Restore);
        customer.Archive(Now);
        Assert.Throws<DomainException>(() => customer.Archive(Now));
        customer.Restore();
        Assert.False(customer.IsArchived);
    }

    [Fact]
    public void Tag_names_compare_without_case_or_accents_and_colours_come_from_the_palette()
    {
        var tag = new Tag(Guid.NewGuid(), " Clientes VIP ", "amber", Now);

        Assert.Equal("Clientes VIP", tag.Name);
        Assert.Equal("clientes vip", tag.NormalizedName);
        Assert.Equal("tag_color_unknown", Assert.Throws<DomainException>(() => tag.Update("VIP", "#ff0000")).Code);
    }
}

public sealed class ProductDomainTests
{
    private static ProductDetails Details(ProductKind kind = ProductKind.Product, bool track = true, decimal price = 2.5m,
        string unit = "unit") =>
        new(kind, " pan-001 ", " Pan ", " ", null, unit, price, 10m, track, 4m);

    [Fact]
    public void The_sku_is_unique_in_upper_case_and_services_drop_stock_settings()
    {
        var product = new Product(Guid.NewGuid(), Details(), 1m, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal("pan-001", product.Sku);
        Assert.Equal("PAN-001", product.NormalizedSku);
        Assert.Null(product.Description);
        Assert.Equal(4m, product.MinStock);

        product.Update(Details(ProductKind.Product, track: false), Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(0m, product.MinStock);
    }

    [Theory]
    [InlineData(ProductKind.Service, true, 1, "unit", "service_without_stock")]
    [InlineData(ProductKind.Product, true, -1, "unit", "amount_invalid")]
    [InlineData(ProductKind.Product, true, 1, "barrel", "unit_unknown")]
    public void Invalid_products_are_refused(ProductKind kind, bool track, decimal price, string unit, string code) =>
        Assert.Equal(code, Assert.Throws<DomainException>(() =>
            new Product(Guid.NewGuid(), Details(kind, track, price, unit), null, Guid.NewGuid(), DateTimeOffset.UtcNow)).Code);
}

public sealed class ImageFormatTests
{
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, "image/webp")]
    [InlineData(new byte[] { (byte)'<', (byte)'s', (byte)'v', (byte)'g' }, null)]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' }, null)]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'A', (byte)'V', (byte)'I', (byte)' ' }, null)]
    [InlineData(new byte[0], null)]
    public void Logos_are_recognised_by_their_first_bytes(byte[] header, string? contentType) =>
        Assert.Equal(contentType, ImageFormats.Detect(header));
}
