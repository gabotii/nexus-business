namespace Nexus.Domain.Common;

/// <summary>
/// Limits of the stored decimals (ADR-0006): money is numeric(19,4), quantities numeric(18,3), rates numeric(5,2).
/// Values with more decimals than the column are rejected rather than silently rounded.
/// </summary>
public static class Amounts
{
    public const int MoneyScale = 4;
    public const int QuantityScale = 3;
    public const int RateScale = 2;

    public const decimal MaxMoney = 999_999_999_999_999.9999m;
    public const decimal MaxQuantity = 999_999_999_999_999.999m;

    /// <summary>True when <paramref name="value"/> has at most <paramref name="scale"/> significant decimals.</summary>
    public static bool FitsScale(decimal value, int scale) => decimal.Round(value, scale) == value;

    public static bool IsValidMoney(decimal value) => value is >= 0 and <= MaxMoney && FitsScale(value, MoneyScale);

    public static bool IsValidQuantity(decimal value) => value is >= 0 and <= MaxQuantity && FitsScale(value, QuantityScale);

    public static bool IsValidRate(decimal value) => value is >= 0 and <= 100 && FitsScale(value, RateScale);
}
