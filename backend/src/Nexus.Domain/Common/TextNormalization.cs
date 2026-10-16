using System.Globalization;
using System.Text;

namespace Nexus.Domain.Common;

/// <summary>
/// Normalised forms used for search and duplicate detection. They are computed in code (not with database functions)
/// so the stored column, its trigram index and the query parameter always agree.
/// </summary>
public static class TextNormalization
{
    /// <summary>"  Panadería  NÚÑEZ " → "panaderia nunez": lower case, no diacritics, single spaces.</summary>
    public static string ForSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Joins the searchable fields of a record into one normalised string.</summary>
    public static string SearchDocument(params string?[] parts) =>
        string.Join(' ', parts.Select(ForSearch).Where(p => p.Length > 0));

    /// <summary>Tax identifiers compare without spaces, dots, dashes or slashes and in upper case ("B-12.345.678" → "B12345678").</summary>
    public static string? TaxId(string? taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId))
        {
            return null;
        }

        var builder = new StringBuilder(taxId.Length);
        foreach (var c in taxId)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>E-mail addresses compare trimmed and case-insensitively.</summary>
    public static string? Email(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();

    /// <summary>Trimmed text, or null when empty: optional fields never store "".</summary>
    public static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
