namespace Nexus.Domain.Common;

/// <summary>
/// ISO 4217 currencies a company may operate in, with their minor-unit precision (ADR-0006: amounts are rounded per
/// line to this number of decimals). The list is deliberately short; adding a currency is a code change because it
/// must come with its precision.
/// </summary>
public static class Currencies
{
    private static readonly Dictionary<string, int> Decimals = new(StringComparer.Ordinal)
    {
        ["EUR"] = 2,
        ["USD"] = 2,
        ["GBP"] = 2,
        ["CHF"] = 2,
        ["MXN"] = 2,
        ["ARS"] = 2,
        ["COP"] = 2,
        ["PEN"] = 2,
        ["UYU"] = 2,
        ["CLP"] = 0,
        ["JPY"] = 0,
    };

    public const string Default = "EUR";

    public static IReadOnlyCollection<string> Supported => Decimals.Keys;

    public static bool IsSupported(string? code) => code is not null && Decimals.ContainsKey(code);

    public static int MinorUnits(string code) =>
        Decimals.TryGetValue(code, out var decimals)
            ? decimals
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unsupported currency.");
}
